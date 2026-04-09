using System.Text;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhListPods : FkhServiceBase
{
    public FkhListPods(ILogger<FkhListPods> logger) : base(logger) { }

    public async Task<string> ListPodsAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var showAll = parameters.TryGetValue("all", out var allValue)
            && string.Equals(allValue, "true", StringComparison.OrdinalIgnoreCase);

        var client = await GetKubernetesClientAsync();
        var allDeployments = await client.ListNamespacedDeploymentAsync(Namespace);

        // Filter to BC service tier deployments (those whose pod template has app-type=windows-servicetier)
        var deployments = allDeployments.Items
            .Where(d => d.Spec.Template.Metadata.Labels != null
                && d.Spec.Template.Metadata.Labels.TryGetValue("app-type", out var appType)
                && appType == "windows-servicetier")
            .ToList();

        var usernamePrefix = $"{githubUsername.ToLowerInvariant()}-";

        var filtered = showAll
            ? deployments
            : deployments.Where(d =>
            {
                var appLabel = d.Spec.Template.Metadata.Labels.TryGetValue("app", out var app) ? app : "";
                return appLabel.StartsWith(usernamePrefix, StringComparison.OrdinalIgnoreCase);
            }).ToList();

        if (filtered.Count == 0)
        {
            return showAll ? "No pods found." : $"No pods found for user '{githubUsername}'. Use --all to list all pods.";
        }

        // Get pod metrics if available
        Dictionary<string, PodMetrics> podMetricsMap = new();
        try
        {
            var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: "app-type=windows-servicetier");
            foreach (var pod in pods.Items)
            {
                try
                {
                    var metrics = await client.GetNamespacedCustomObjectAsync<PodMetrics>(
                        "metrics.k8s.io", "v1beta1", Namespace, "pods", pod.Metadata.Name);
                    if (metrics != null)
                        podMetricsMap[pod.Metadata.Labels["app"]] = metrics;
                }
                catch { /* metrics-server may not be available */ }
            }
        }
        catch { /* metrics API not available */ }

        var sb = new StringBuilder();
        sb.Append(showAll ? "All pods:" : $"Pods for '{githubUsername}':");

        // Get services to resolve FQDNs
        var services = await client.ListNamespacedServiceAsync(Namespace);
        var serviceMap = services.Items
            .Where(s => s.Spec?.Selector != null && s.Spec.Selector.ContainsKey("app"))
            .GroupBy(s => s.Spec.Selector["app"])
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var deployment in filtered)
        {
            var appLabel = deployment.Spec.Template.Metadata.Labels["app"];
            var deploymentName = deployment.Metadata.Name;
            var replicas = deployment.Spec.Replicas ?? 0;
            var readyReplicas = deployment.Status?.ReadyReplicas ?? 0;
            var image = deployment.Spec.Template.Spec.Containers.FirstOrDefault()?.Image ?? "unknown";
            var container = deployment.Spec.Template.Spec.Containers.FirstOrDefault();

            // Strip registry prefix for readability
            var shortImage = image.Contains('/') ? image[(image.LastIndexOf('/') + 1)..] : image;

            var status = replicas == 0 ? "Stopped" : readyReplicas >= replicas ? "Running" : "Starting";

            // Extract pod name by stripping the "username-" prefix
            var podName = appLabel.Contains('-') && appLabel.IndexOf('-') < appLabel.Length - 1
                ? appLabel[(appLabel.IndexOf('-') + 1)..] : appLabel;

            sb.Append($"\n\n  {appLabel}");
            sb.Append($"\n    Name:   {podName}");
            sb.Append($"\n    Status: {status} ({readyReplicas}/{replicas} ready)");
            sb.Append($"\n    Image:  {shortImage}");

            // Auto-stop time
            if (deployment.Metadata.Annotations?.TryGetValue("fkh/auto-stop-at", out var stopAtStr) == true
                && DateTimeOffset.TryParse(stopAtStr, out var stopAt))
            {
                var remaining = stopAt - DateTimeOffset.UtcNow;
                var timeLeft = remaining.TotalMinutes > 0
                    ? $"in {remaining.Hours}h{remaining.Minutes:D2}m"
                    : "overdue";
                sb.Append($"\n    AutoStop: {stopAt:yyyy-MM-dd HH:mm} UTC ({timeLeft})");
            }

            // Repo and project metadata
            if (deployment.Metadata.Annotations?.TryGetValue("fkh/repo", out var repo) == true && !string.IsNullOrEmpty(repo))
                sb.Append($"\n    Repo:    {repo}");
            if (deployment.Metadata.Annotations?.TryGetValue("fkh/project", out var proj) == true && !string.IsNullOrEmpty(proj))
                sb.Append($"\n    Project: {proj}");

            // Web client URL from service FQDN
            if (serviceMap.TryGetValue(appLabel, out var svc))
            {
                var dnsLabel = svc.Metadata.Annotations?.TryGetValue("service.beta.kubernetes.io/azure-dns-label-name", out var label) == true ? label : null;
                if (dnsLabel != null)
                {
                    sb.Append($"\n    WebClient:    https://{dnsLabel}.{AksLocation}.cloudapp.azure.com/BC/");
                }
            }

            if (podMetricsMap.TryGetValue(appLabel, out var metrics))
            {
                var containerMetrics = metrics.Containers?.FirstOrDefault();
                if (containerMetrics?.Usage != null)
                {
                    if (containerMetrics.Usage.TryGetValue("cpu", out var cpuVal))
                    {
                        var usedCores = cpuVal.ToDouble();
                        var limitCores = container?.Resources?.Limits?.TryGetValue("cpu", out var cpuLim) == true
                            ? cpuLim.ToDouble() : 0;
                        if (limitCores > 0)
                        {
                            var pct = usedCores / limitCores * 100;
                            sb.Append($"\n    CPU:    {pct:F0}% (of {limitCores:G} cores)");
                        }
                        else
                        {
                            var milliCores = usedCores * 1000;
                            sb.Append($"\n    CPU:    {milliCores:F0}m");
                        }
                    }

                    if (containerMetrics.Usage.TryGetValue("memory", out var memVal))
                    {
                        var usedBytes = memVal.ToDouble();
                        var limitBytes = container?.Resources?.Limits?.TryGetValue("memory", out var limVal) == true
                            ? limVal.ToDouble() : 0;
                        var usedMb = usedBytes / (1024 * 1024);
                        if (limitBytes > 0)
                        {
                            var limitMb = limitBytes / (1024 * 1024);
                            sb.Append($"\n    Memory: {usedMb:F0}Mb used (of {limitMb:F0}Mb)");
                        }
                        else
                        {
                            sb.Append($"\n    Memory: {usedMb:F0}Mb used");
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }

    // Lightweight models for pod metrics API
    private class PodMetrics
    {
        public List<ContainerMetrics>? Containers { get; set; }
    }

    private class ContainerMetrics
    {
        public string? Name { get; set; }
        public Dictionary<string, ResourceQuantity>? Usage { get; set; }
    }
}
