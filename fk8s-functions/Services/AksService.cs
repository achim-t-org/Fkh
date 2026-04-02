namespace FK8s.Services;

/// <summary>
/// Handles AKS operations using the Function's Managed Identity.
/// The Managed Identity must be assigned the appropriate RBAC role on the AKS cluster
/// (e.g. "Azure Kubernetes Service Contributor" or a custom role scoped to node pool operations).
///
/// TODO: Implement the specific AKS operation once you've decided what "create a node" means:
///   - Scale an existing node pool (increase node count)
///   - Add a new node pool
///   - Something else
/// </summary>
public class AksService
{
    // AKS cluster details come from environment variables / App Settings,
    // set via Terraform — no hardcoded values here.
    private readonly string _resourceGroup;
    private readonly string _clusterName;
    private readonly string _subscriptionId;

    public AksService()
    {
        _subscriptionId = Environment.GetEnvironmentVariable("AKS_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("AKS_SUBSCRIPTION_ID is not configured.");
        _resourceGroup = Environment.GetEnvironmentVariable("AKS_RESOURCE_GROUP")
            ?? throw new InvalidOperationException("AKS_RESOURCE_GROUP is not configured.");
        _clusterName = Environment.GetEnvironmentVariable("AKS_CLUSTER_NAME")
            ?? throw new InvalidOperationException("AKS_CLUSTER_NAME is not configured.");
    }

    /// <summary>
    /// Placeholder for the AKS node creation operation.
    /// Managed Identity authentication to Azure is handled automatically at the infrastructure
    /// level — no credentials needed in code.
    /// </summary>
    public Task<string> CreateNodeAsync(Dictionary<string, string> parameters)
    {
        return Task.FromResult("Hello World");
    }

    public Task<string> RemoveNodeAsync(Dictionary<string, string> parameters)
    {
        return Task.FromResult("Hello World");
    }
}
