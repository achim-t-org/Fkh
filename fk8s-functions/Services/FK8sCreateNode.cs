using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sCreateNode : FK8sServiceBase
{
    private readonly GitHubAppTokenService _gitHubAppTokenService;

    public FK8sCreateNode(ILogger<FK8sCreateNode> logger, GitHubAppTokenService gitHubAppTokenService) : base(logger)
    {
        _gitHubAppTokenService = gitHubAppTokenService;
    }

    public async Task<string> CreateNodeAsync(Dictionary<string, string> parameters)
    {
        var artifactUrl = parameters["artifactUrl"];
        var adminUsername = parameters["adminUsername"];
        var adminPassword = parameters["adminPassword"];

        var imageTag = GetImageTag(artifactUrl);
        var fullImage = $"{AcrLoginServer}/{AcrRepository}:{imageTag}";
        var appName = SanitizeAppName(imageTag);

        Logger.LogInformation("Checking ACR for image {Image}", fullImage);
        await EnsureImageExistsAsync(imageTag, fullImage, artifactUrl);

        Logger.LogInformation("Image found. Creating Kubernetes resources for {AppName}...", appName);
        var client = await GetKubernetesClientAsync();

        var secretName = $"{appName}-secret";
        var deploymentName = $"{appName}-deployment";
        var serviceName = $"{appName}-service";

        await CreateAdminSecretAsync(client, secretName, adminPassword);

        var dnsLabel = appName;
        var publicDnsName = $"{dnsLabel}.{AksLocation}.cloudapp.azure.com";

        await CreateDeploymentAsync(client, deploymentName, appName, fullImage, adminUsername, secretName, publicDnsName);
        await CreateLoadBalancerServiceAsync(client, serviceName, appName, dnsLabel);

        Logger.LogInformation("Deployment {Deployment} and service {Service} created in namespace {Namespace}",
            deploymentName, serviceName, Namespace);

        return $"Node created. Deployment: {deploymentName}, Service: {serviceName}, Image: {fullImage}, FQDN: {publicDnsName}";
    }

    private async Task EnsureImageExistsAsync(string tag, string fullImage, string artifactUrl)
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var client = new ContainerRegistryClient(new Uri($"https://{AcrLoginServer}"), credential);

        try
        {
            var artifact = client.GetArtifact(AcrRepository, tag);
            await artifact.GetManifestPropertiesAsync();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            Logger.LogInformation("Image not found in ACR. Triggering createImages workflow for {ArtifactUrl}...", artifactUrl);
            await _gitHubAppTokenService.TriggerCreateImagesWorkflowAsync(artifactUrl);
            throw new InvalidOperationException(
                $"Image does not exist yet: {fullImage}. The createImages workflow has been triggered automatically. " +
                $"Please retry in a few minutes once the image is built.");
        }
    }

    private async Task CreateAdminSecretAsync(Kubernetes client, string secretName, string adminPassword)
    {
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
    }

    private async Task CreateDeploymentAsync(
        Kubernetes client, string deploymentName, string appName, string fullImage,
        string adminUsername, string secretName, string publicDnsName)
    {
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
                                    new(80), new(443), new(7047), new(7048), new(7049),
                                },
                                Env = BuildEnvVars(adminUsername, secretName, publicDnsName)
                            }
                        }
                    }
                }
            }
        };

        try { await client.DeleteNamespacedDeploymentAsync(deploymentName, Namespace); } catch { /* may not exist */ }
        await client.CreateNamespacedDeploymentAsync(deployment, Namespace);
    }

    private List<V1EnvVar> BuildEnvVars(string adminUsername, string secretName, string publicDnsName)
    {
        return new List<V1EnvVar>
        {
            new() { Name = "accept_eula", Value = "Y" },
            new() { Name = "username", Value = adminUsername },
            new()
            {
                Name = "password",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector { Name = secretName, Key = "password" }
                }
            },
            new() { Name = "publicDnsName", Value = publicDnsName },
            new() { Name = "contactEMailForLetsEncrypt", Value = ContactEmail },
            new() { Name = "folders", Value = FoldersValue },
            //,
            // new()
            // {
            //     Name = "databasePassword",
            //     ValueFrom = new V1EnvVarSource
            //     {
            //         SecretKeyRef = new V1SecretKeySelector { Name = "mssql-secret", Key = "sa-password" }
            //     }
            // },
            // new() { Name = "databaseUsername", Value = "sa" },
            // new() { Name = "databaseServer", Value = $"mssql-service.{Namespace}.svc.cluster.local" },
            // new() { Name = "databaseInstance", Value = "" },
        };
    }

    private async Task CreateLoadBalancerServiceAsync(Kubernetes client, string serviceName, string appName, string dnsLabel)
    {
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = Namespace,
                Annotations = new Dictionary<string, string>
                {
                    ["service.beta.kubernetes.io/azure-dns-label-name"] = dnsLabel
                }
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
    }
}
