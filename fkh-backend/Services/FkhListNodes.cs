using System.Text;
using k8s;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhListNodes : FkhServiceBase
{
    public FkhListNodes(ILogger<FkhListNodes> logger) : base(logger) { }

    public async Task<string> ListNodesAsync(Dictionary<string, string> parameters)
    {
        var isAdmin = parameters.TryGetValue("_isAdmin", out var adminValue)
            && string.Equals(adminValue, "true", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            throw new UnauthorizedAccessException("ListNodes is restricted to administrators.");
        }

        var client = await GetKubernetesClientAsync();
        var nodes = await client.ListNodeAsync();

        var windowsNodes = nodes.Items
            .Where(n => n.Metadata.Labels.TryGetValue("kubernetes.io/os", out var os) && os == "windows")
            .OrderBy(n => n.Metadata.Name)
            .ToList();

        if (windowsNodes.Count == 0)
        {
            return "No Windows nodes found.";
        }

        // Check CNS status per node
        var allKubeSystemPods = await client.ListNamespacedPodAsync("kube-system");
        var cnsPods = allKubeSystemPods.Items
            .Where(p => p.Metadata.Name.StartsWith("azure-cns", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Get pod counts per node in app namespace
        var appPods = await client.ListNamespacedPodAsync(Namespace);

        var sb = new StringBuilder();
        foreach (var node in windowsNodes)
        {
            var name = node.Metadata.Name;
            var ready = node.Status.Conditions
                .Any(c => c.Type == "Ready" && c.Status == "True");
            var kubeletVersion = node.Status.NodeInfo.KubeletVersion;
            var osImage = node.Status.NodeInfo.OsImage;

            // CPU and memory capacity
            var cpuCapacity = node.Status.Capacity.TryGetValue("cpu", out var cpu) ? cpu.ToString() : "?";
            var memCapacity = node.Status.Capacity.TryGetValue("memory", out var mem) ? FormatMemory(mem.ToString()) : "?";
            var cpuAllocatable = node.Status.Allocatable.TryGetValue("cpu", out var cpuA) ? cpuA.ToString() : "?";
            var memAllocatable = node.Status.Allocatable.TryGetValue("memory", out var memA) ? FormatMemory(memA.ToString()) : "?";

            // CNS running on this node?
            var cnsReady = cnsPods.Any(p =>
                p.Spec.NodeName == name
                && p.Status.Phase == "Running"
                && p.Status.ContainerStatuses?.All(c => c.Ready) == true);

            // Pod count on this node
            var podCount = appPods.Items.Count(p => p.Spec.NodeName == name);

            sb.AppendLine($"  {name}");
            sb.AppendLine($"    Status: {(ready ? "Ready" : "NotReady")}");
            sb.AppendLine($"    CNS: {(cnsReady ? "Ready" : "NotReady")}");
            sb.AppendLine($"    Pods: {podCount}");
            sb.AppendLine($"    CPU: {cpuAllocatable}/{cpuCapacity}");
            sb.AppendLine($"    Memory: {memAllocatable}/{memCapacity}");
            sb.AppendLine($"    Kubelet: {kubeletVersion}");
            sb.AppendLine($"    OS: {osImage}");
        }

        return sb.ToString();
    }

    private static string FormatMemory(string memKi)
    {
        // Kubernetes reports memory in Ki (kibibytes)
        if (memKi.EndsWith("Ki") && long.TryParse(memKi[..^2], out var ki))
        {
            var gi = ki / (1024.0 * 1024.0);
            return $"{gi:F1}Gi";
        }
        return memKi;
    }
}
