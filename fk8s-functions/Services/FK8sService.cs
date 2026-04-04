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

    public Task<string> CreateNodeAsync(Dictionary<string, string> parameters)
    {
        return Task.FromResult("Hello World");
    }

    public Task<string> RemoveNodeAsync(Dictionary<string, string> parameters)
    {
        var nodeUrl = parameters["NodeUrl"];

        return Task.FromResult("Hello World");
    }
}
