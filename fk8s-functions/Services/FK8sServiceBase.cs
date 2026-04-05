using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using k8s;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public abstract class FK8sServiceBase
{
    protected readonly string ResourceGroup;
    protected readonly string ClusterName;
    protected readonly string SubscriptionId;
    protected readonly string AcrName;
    protected readonly string ClientId;
    protected readonly string BaseImage;
    protected readonly string AksLocation;
    protected readonly string ContactEmail;
    protected readonly ILogger Logger;

    protected const string Namespace = "app";
    protected const string AcrRepository = "businesscentral";
    protected const string FoldersValue = @"c:\run\my=https://github.com/Freddy-DK/ContainerScripts/archive/refs/heads/main.zip\ContainerScripts-main";

    protected FK8sServiceBase(ILogger logger)
    {
        Logger = logger;
        SubscriptionId = Environment.GetEnvironmentVariable("AKS_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("AKS_SUBSCRIPTION_ID is not configured.");
        ResourceGroup = Environment.GetEnvironmentVariable("AKS_RESOURCE_GROUP")
            ?? throw new InvalidOperationException("AKS_RESOURCE_GROUP is not configured.");
        ClusterName = Environment.GetEnvironmentVariable("AKS_CLUSTER_NAME")
            ?? throw new InvalidOperationException("AKS_CLUSTER_NAME is not configured.");
        AcrName = Environment.GetEnvironmentVariable("ACR_NAME")
            ?? throw new InvalidOperationException("ACR_NAME is not configured.");
        ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            ?? throw new InvalidOperationException("AZURE_CLIENT_ID is not configured.");
        BaseImage = Environment.GetEnvironmentVariable("BASE_IMAGE")
            ?? throw new InvalidOperationException("BASE_IMAGE is not configured.");
        AksLocation = Environment.GetEnvironmentVariable("AKS_LOCATION")
            ?? throw new InvalidOperationException("AKS_LOCATION is not configured.");
        ContactEmail = Environment.GetEnvironmentVariable("CONTACT_EMAIL_FOR_LETSENCRYPT")
            ?? throw new InvalidOperationException("CONTACT_EMAIL_FOR_LETSENCRYPT is not configured.");
    }

    protected string AcrLoginServer => $"{AcrName}.azurecr.io";

    protected async Task<Kubernetes> GetKubernetesClientAsync()
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var armClient = new ArmClient(credential);

        var aksId = ContainerServiceManagedClusterResource
            .CreateResourceIdentifier(SubscriptionId, ResourceGroup, ClusterName);
        var cluster = armClient.GetContainerServiceManagedClusterResource(aksId);
        var creds = await cluster.GetClusterUserCredentialsAsync();
        var kubeconfig = creds.Value.Kubeconfigs[0].Value;

        using var stream = new MemoryStream(kubeconfig);
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
        return new Kubernetes(config);
    }

    protected static string GetImageTag(string artifactUrl)
    {
        var uri = new Uri(artifactUrl);
        return uri.AbsolutePath.Replace('/', '-').TrimStart('-').ToLowerInvariant();
    }

    protected static string SanitizeAppName(string imageTag)
    {
        var appName = $"bc-{imageTag}".Replace('.', '-');
        if (appName.Length > 63) appName = appName[..63];
        return appName.TrimEnd('-');
    }
}
