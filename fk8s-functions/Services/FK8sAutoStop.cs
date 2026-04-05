using k8s;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sAutoStop : FK8sServiceBase
{
    public FK8sAutoStop(ILogger<FK8sAutoStop> logger) : base(logger) { }

    public async Task CheckAndStopExpiredNodesAsync()
    {
        Logger.LogInformation("Auto-stop check started at {Time} UTC", DateTimeOffset.UtcNow);
        var client = await GetKubernetesClientAsync();
        var allDeployments = await client.ListNamespacedDeploymentAsync(Namespace);

        var stopped = 0;
        foreach (var deployment in allDeployments.Items)
        {
            if (deployment.Metadata.Annotations == null ||
                !deployment.Metadata.Annotations.TryGetValue(AutoStopAnnotation, out var stopAtStr))
                continue;

            if (!DateTimeOffset.TryParse(stopAtStr, out var stopAt))
            {
                Logger.LogWarning("Invalid auto-stop annotation value '{Value}' on deployment '{Deployment}'.",
                    stopAtStr, deployment.Metadata.Name);
                continue;
            }

            // Skip already-stopped deployments (replicas == 0)
            if ((deployment.Spec.Replicas ?? 0) == 0)
                continue;

            if (DateTimeOffset.UtcNow >= stopAt)
            {
                Logger.LogInformation("Auto-stopping deployment '{Deployment}' (scheduled at {StopAt} UTC).",
                    deployment.Metadata.Name, stopAt);

                deployment.Spec.Replicas = 0;
                deployment.Metadata.Annotations.Remove(AutoStopAnnotation);
                await client.ReplaceNamespacedDeploymentAsync(deployment, deployment.Metadata.Name, Namespace);
                stopped++;
            }
        }

        Logger.LogInformation("Auto-stop check complete. Stopped {Count} deployment(s).", stopped);
    }
}
