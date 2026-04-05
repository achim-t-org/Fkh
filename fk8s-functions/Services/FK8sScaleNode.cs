using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sScaleNode : FK8sServiceBase
{
    public FK8sScaleNode(ILogger<FK8sScaleNode> logger) : base(logger) { }

    public async Task<string> StopNodeAsync(Dictionary<string, string> parameters)
    {
        var result = await ScaleAsync(parameters, 0);
        // Clear auto-stop annotation when manually stopping
        var name = parameters["name"];
        var githubUsername = parameters["_githubUsername"];
        var appName = SanitizeAppName($"{githubUsername}-{name}");
        var deploymentName = $"{appName}-deployment";
        var client = await GetKubernetesClientAsync();
        await ClearAutoStopAnnotationAsync(client, deploymentName);
        return result;
    }

    public async Task<string> StartNodeAsync(Dictionary<string, string> parameters)
    {
        var result = await ScaleAsync(parameters, 1);

        // Set auto-stop annotation if requested
        var name = parameters["name"];
        var githubUsername = parameters["_githubUsername"];
        var appName = SanitizeAppName($"{githubUsername}-{name}");
        var deploymentName = $"{appName}-deployment";
        var autoStopInfo = "";

        if (parameters.TryGetValue("autostop", out var autoStopHours) && double.TryParse(autoStopHours, out var hours) && hours > 0)
        {
            var stopAt = DateTimeOffset.UtcNow.AddHours(hours);
            var client = await GetKubernetesClientAsync();
            await SetAutoStopAnnotationAsync(client, deploymentName, stopAt);
            autoStopInfo = $"\n  AutoStop: {stopAt:yyyy-MM-dd HH:mm} UTC ({hours}h)";
        }

        return result + autoStopInfo;
    }

    private async Task<string> ScaleAsync(Dictionary<string, string> parameters, int replicas)
    {
        var name = parameters["name"];
        var githubUsername = parameters["_githubUsername"];
        var appName = SanitizeAppName($"{githubUsername}-{name}");
        var deploymentName = $"{appName}-deployment";

        Logger.LogInformation("Scaling deployment '{Deployment}' to {Replicas} replicas...", deploymentName, replicas);
        var client = await GetKubernetesClientAsync();

        var deployment = await client.ReadNamespacedDeploymentAsync(deploymentName, Namespace);
        deployment.Spec.Replicas = replicas;
        await client.ReplaceNamespacedDeploymentAsync(deployment, deploymentName, Namespace);

        var action = replicas == 0 ? "Stopped" : "Started";
        Logger.LogInformation("{Action} deployment '{Deployment}'.", action, deploymentName);
        return $"{action} node '{appName}'.\n  Deployment: {deploymentName}\n  Replicas: {replicas}";
    }
}
