using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.ContainerService.Models;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sService
{
    private readonly string _resourceGroup;
    private readonly string _clusterName;
    private readonly string _subscriptionId;
    private readonly string _acrName;
    private readonly string _clientId;
    private readonly string _baseImage;
    private readonly ILogger<FK8sService> _logger;

    private const string Namespace = "app";
    private const string AcrRepository = "businesscentral";

    public FK8sService(ILogger<FK8sService> logger)
    {
        _logger = logger;
        _subscriptionId = Environment.GetEnvironmentVariable("AKS_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("AKS_SUBSCRIPTION_ID is not configured.");
        _resourceGroup = Environment.GetEnvironmentVariable("AKS_RESOURCE_GROUP")
            ?? throw new InvalidOperationException("AKS_RESOURCE_GROUP is not configured.");
        _clusterName = Environment.GetEnvironmentVariable("AKS_CLUSTER_NAME")
            ?? throw new InvalidOperationException("AKS_CLUSTER_NAME is not configured.");
        _acrName = Environment.GetEnvironmentVariable("ACR_NAME")
            ?? throw new InvalidOperationException("ACR_NAME is not configured.");
        _clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            ?? throw new InvalidOperationException("AZURE_CLIENT_ID is not configured.");
        _baseImage = Environment.GetEnvironmentVariable("BASE_IMAGE")
            ?? throw new InvalidOperationException("BASE_IMAGE is not configured.");
    }

    /// <summary>
    /// Derives the ACR image tag from an artifact URL using the same convention
    /// as the createImages workflow: AbsolutePath with '/' replaced by '-', trimmed, lowered.
    /// </summary>
    private static string GetImageTag(string artifactUrl)
    {
        var uri = new Uri(artifactUrl);
        return uri.AbsolutePath.Replace('/', '-').TrimStart('-').ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether a tag exists in the ACR repository.
    /// </summary>
    private async Task<bool> ImageExistsInAcrAsync(string tag)
    {
#pragma warning disable CS0618 // ManagedIdentityCredential string ctor is deprecated but functional
        var credential = new ManagedIdentityCredential(_clientId);
#pragma warning restore CS0618
        var acrLoginServer = $"{_acrName}.azurecr.io";
        var client = new ContainerRegistryClient(new Uri($"https://{acrLoginServer}"), credential);

        try
        {
            var artifact = client.GetArtifact(AcrRepository, tag);
            var properties = await artifact.GetManifestPropertiesAsync();
            return properties != null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a Kubernetes client authenticated via the managed identity's AKS credentials.
    /// </summary>
    private async Task<Kubernetes> GetKubernetesClientAsync()
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(_clientId);
#pragma warning restore CS0618
        var armClient = new ArmClient(credential);

        // Use kubeconfig via Azure REST to get cluster credentials
        var aksId = ContainerServiceManagedClusterResource
            .CreateResourceIdentifier(_subscriptionId, _resourceGroup, _clusterName);
        var cluster = armClient.GetContainerServiceManagedClusterResource(aksId);
        var creds = await cluster.GetClusterUserCredentialsAsync();
        var kubeconfig = creds.Value.Kubeconfigs[0].Value;

        using var stream = new MemoryStream(kubeconfig);
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
        return new Kubernetes(config);
    }

    public async Task<string> CreateNodeAsync(Dictionary<string, string> parameters)
    {
        var artifactUrl = parameters["artifactUrl"];
        var adminUsername = parameters["adminUsername"];
        var adminPassword = parameters["adminPassword"];

        var imageTag = GetImageTag(artifactUrl);
        var acrLoginServer = $"{_acrName}.azurecr.io";
        var fullImage = $"{acrLoginServer}/{AcrRepository}:{imageTag}";

        _logger.LogInformation("Checking ACR for image {Image}", fullImage);

        // ── Check if image exists ────────────────────────────────────────────
        if (!await ImageExistsInAcrAsync(imageTag))
        {
            throw new InvalidOperationException(
                $"Image does not exist: {fullImage}. Run the CreateImages workflow first with artifactUrl: {artifactUrl}");
        }

        _logger.LogInformation("Image found. Creating Kubernetes deployment...");

        var client = await GetKubernetesClientAsync();

        // Use a sanitized name derived from the tag
        var appName = $"bc-{imageTag}";
        if (appName.Length > 63) appName = appName[..63];
        appName = appName.TrimEnd('-');

        var deploymentName = $"{appName}-deployment";
        var serviceName = $"{appName}-service";
        var secretName = $"{appName}-secret";

        // ── Create secret for admin password ─────────────────────────────────
        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta { Name = secretName, NamespaceProperty = Namespace },
            Type = "Opaque",
            Data = new Dictionary<string, byte[]>
            {
                ["password"] = System.Text.Encoding.UTF8.GetBytes(adminPassword)
            }
        };

        try { await client.DeleteNamespacedSecretAsync(secretName, Namespace); } catch { /* may not exist */ }
        await client.CreateNamespacedSecretAsync(secret, Namespace);

        // ── Create deployment ────────────────────────────────────────────────
        var deployment = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = deploymentName, NamespaceProperty = Namespace },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = appName }
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = appName,
                            ["app-type"] = "windows-servicetier"
                        }
                    },
                    Spec = new V1PodSpec
                    {
                        NodeSelector = new Dictionary<string, string>
                        {
                            ["kubernetes.io/os"] = "windows"
                        },
                        Containers = new List<V1Container>
                        {
                            new()
                            {
                                Name = appName,
                                Image = fullImage,
                                Ports = new List<V1ContainerPort>
                                {
                                    new(80),
                                    new(443),
                                    new(7047),
                                    new(7048),
                                    new(7049),
                                },
                                Env = new List<V1EnvVar>
                                {
                                    new() { Name = "accept_eula", Value = "Y" },
                                    new() { Name = "username", Value = adminUsername },
                                    new()
                                    {
                                        Name = "password",
                                        ValueFrom = new V1EnvVarSource
                                        {
                                            SecretKeyRef = new V1SecretKeySelector
                                            {
                                                Name = secretName,
                                                Key = "password"
                                            }
                                        }
                                    },
                                    new() { Name = "databasePassword", ValueFrom = new V1EnvVarSource
                                        {
                                            SecretKeyRef = new V1SecretKeySelector
                                            {
                                                Name = "mssql-secret",
                                                Key = "sa-password"
                                            }
                                        }
                                    },
                                    new() { Name = "databaseUsername", Value = "sa" },
                                    new() { Name = "databaseServer", Value = $"mssql-service.{Namespace}.svc.cluster.local" },
                                    new() { Name = "databaseInstance", Value = "" },
                                }
                            }
                        }
                    }
                }
            }
        };

        try { await client.DeleteNamespacedDeploymentAsync(deploymentName, Namespace); } catch { /* may not exist */ }
        await client.CreateNamespacedDeploymentAsync(deployment, Namespace);

        // ── Create LoadBalancer service ──────────────────────────────────────
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = Namespace
            },
            Spec = new V1ServiceSpec
            {
                Type = "LoadBalancer",
                Selector = new Dictionary<string, string> { ["app"] = appName },
                Ports = new List<V1ServicePort>
                {
                    new() { Name = "http", Port = 80, TargetPort = 80 },
                    new() { Name = "https", Port = 443, TargetPort = 443 },
                    new() { Name = "odata", Port = 7047, TargetPort = 7047 },
                    new() { Name = "soap", Port = 7048, TargetPort = 7048 },
                    new() { Name = "dev", Port = 7049, TargetPort = 7049 },
                }
            }
        };

        try { await client.DeleteNamespacedServiceAsync(serviceName, Namespace); } catch { /* may not exist */ }
        await client.CreateNamespacedServiceAsync(service, Namespace);

        _logger.LogInformation("Deployment {Deployment} and service {Service} created in namespace {Namespace}",
            deploymentName, serviceName, Namespace);

        return $"Node created. Deployment: {deploymentName}, Service: {serviceName}, Image: {fullImage}";
    }

    public Task<string> RemoveNodeAsync(Dictionary<string, string> parameters)
    {
        var nodeUrl = parameters["NodeUrl"];

        return Task.FromResult("Hello World");
    }
}
