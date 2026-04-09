using k8s;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhGetPodLogs : FkhServiceBase
{
    public FkhGetPodLogs(ILogger<FkhGetPodLogs> logger) : base(logger) { }

    public async Task<string> GetPodLogsAsync(Dictionary<string, string> parameters)
    {
        var name = parameters["name"];
        var githubUsername = parameters["_githubUsername"];
        var isAdmin = parameters.TryGetValue("_isAdmin", out var adminValue)
            && string.Equals(adminValue, "true", StringComparison.OrdinalIgnoreCase);

        var appName = SanitizeAppName($"{githubUsername}-{name}");

        var client = await GetKubernetesClientAsync();
        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={appName}");

        if (pods.Items.Count == 0 && isAdmin)
        {
            // Admin can view any pod — try without username prefix
            var adminAppName = SanitizeAppName(name);
            pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={adminAppName}");
        }

        if (pods.Items.Count == 0)
        {
            return $"No pod found for '{name}'.";
        }

        var pod = pods.Items[0];
        var tailLines = parameters.TryGetValue("tail", out var tailValue) && int.TryParse(tailValue, out var t) ? t : 500;

        var stream = await client.ReadNamespacedPodLogAsync(
            pod.Metadata.Name,
            Namespace,
            container: pod.Spec.Containers[0].Name,
            tailLines: tailLines);

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
