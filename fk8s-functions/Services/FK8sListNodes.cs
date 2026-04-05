using System.Text;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sListNodes : FK8sServiceBase
{
    public FK8sListNodes(ILogger<FK8sListNodes> logger) : base(logger) { }

    public async Task<string> ListNodesAsync(Dictionary<string, string> parameters)
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
            return showAll ? "No nodes found." : $"No nodes found for user '{githubUsername}'. Use --all to list all nodes.";
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
        sb.AppendLine(showAll ? "All nodes:" : $"Nodes for '{githubUsername}':");

        // Get services to resolve FQDNs
        var services = await client.ListNamespacedServiceAsync(Namespace);
        var serviceMap = services.Items
            .Where(s => s.Spec?.Selector != null && s.Spec.Selector.ContainsKey("app"))
            .ToDictionary(s => s.Spec.Selector["app"], s => s);

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

            sb.Append($"\n  {appLabel}");
            sb.Append($"\n    Status: {status} ({readyReplicas}/{replicas} ready)");
            sb.Append($"\n    Image:  {shortImage}");

            // Web client URL from service FQDN
            if (serviceMap.TryGetValue(appLabel, out var svc))
            {
                var dnsLabel = svc.Metadata.Annotations?.TryGetValue("service.beta.kubernetes.io/azure-dns-label-name", out var label) == true ? label : null;
                if (dnsLabel != null)
                {
                    sb.Append($"\n    URL:    https://{dnsLabel}.{AksLocation}.cloudapp.azure.com/BC/");
                }
            }

            if (podMetricsMap.TryGetValue(appLabel, out var metrics))
            {
                var containerMetrics = metrics.Containers?.FirstOrDefault();
                if (containerMetrics?.Usage != null)
                {
                    var cpu = containerMetrics.Usage.TryGetValue("cpu", out var cpuVal) ? cpuVal.ToString() : "n/a";
                    sb.Append($"\n    CPU:    {cpu}");

                    if (containerMetrics.Usage.TryGetValue("memory", out var memVal))
                    {
                        var usedBytes = memVal.ToDouble();
                        var limitBytes = container?.Resources?.Limits?.TryGetValue("memory", out var limVal) == true
                            ? limVal.ToDouble() : 0;
                        if (limitBytes > 0)
                        {
                            var usedMb = usedBytes / (1024 * 1024);
                            var limitMb = limitBytes / (1024 * 1024);
                            sb.Append($"\n    Memory: {usedMb:F0}Mb used (of {limitMb:F0}Mb)");
                        }
                        else
                        {
                            sb.Append($"\n    Memory: {memVal} used");
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
