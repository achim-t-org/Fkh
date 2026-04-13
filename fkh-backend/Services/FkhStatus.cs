using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Azure.Storage.Blobs;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Fkh.Services;

public class FkhStatus : FkhServiceBase
{
    public FkhStatus(ILogger<FkhStatus> logger) : base(logger) { }

    public async Task<object> GetStatusAsync(Dictionary<string, string> parameters)
    {
        var isAdmin = parameters.TryGetValue("_isAdmin", out var adminValue)
            && string.Equals(adminValue, "true", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
            throw new UnauthorizedAccessException("Status is restricted to administrators.");

#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618

        // Run independent data collection tasks in parallel
        var kubeTask = GetKubernetesStatusAsync();
        var storageTask = GetStorageStatusAsync(credential);
        var quotaTask = GetSubscriptionQuotaAsync(credential);
        var securityTask = Task.FromResult(GetSecurityStatus());

        await Task.WhenAll(kubeTask, storageTask, quotaTask, securityTask);

        return new
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kubernetes = await kubeTask,
            Storage = await storageTask,
            Quota = await quotaTask,
            Security = await securityTask,
        };
    }

    private async Task<object> GetKubernetesStatusAsync()
    {
        var client = await GetKubernetesClientAsync();

        // Collect node info, pod info, and metrics concurrently
        var nodesTask = client.ListNodeAsync();
        var appPodsTask = client.ListNamespacedPodAsync(Namespace);
        var allDeploymentsTask = client.ListNamespacedDeploymentAsync(Namespace);

        await Task.WhenAll(nodesTask, appPodsTask, allDeploymentsTask);

        var nodes = await nodesTask;
        var appPods = await appPodsTask;
        var allDeployments = await allDeploymentsTask;

        // ── Linux pods (mssql, system services) ─────────────────────────────────
        var linuxNodes = nodes.Items
            .Where(n => n.Metadata.Labels.TryGetValue("kubernetes.io/os", out var os) && os == "linux")
            .ToList();

        var linuxStatus = new List<object>();
        foreach (var node in linuxNodes)
        {
            var name = node.Metadata.Name;
            var ready = node.Status.Conditions.Any(c => c.Type == "Ready" && c.Status == "True");
            var cpuCap = node.Status.Capacity.TryGetValue("cpu", out var cpu) ? cpu.ToString() : "?";
            var memCap = node.Status.Capacity.TryGetValue("memory", out var mem) ? FormatMemory(mem.ToString()) : "?";
            var cpuAlloc = node.Status.Allocatable.TryGetValue("cpu", out var cpuA) ? cpuA.ToString() : "?";
            var memAlloc = node.Status.Allocatable.TryGetValue("memory", out var memA) ? FormatMemory(memA.ToString()) : "?";
            var diskCap = node.Status.Capacity.TryGetValue("ephemeral-storage", out var disk) ? FormatStorage(disk.ToString()) : "?";
            var diskAlloc = node.Status.Allocatable.TryGetValue("ephemeral-storage", out var diskA) ? FormatStorage(diskA.ToString()) : "?";

            var podsOnNode = appPods.Items.Where(p => p.Spec.NodeName == name)
                .Select(p => p.Metadata.Labels.TryGetValue("app", out var app) ? app : p.Metadata.Name)
                .OrderBy(p => p).ToList();

            linuxStatus.Add(new
            {
                Name = name,
                Status = ready ? "Ready" : "NotReady",
                Cpu = $"{cpuAlloc}/{cpuCap}",
                Memory = $"{memAlloc}/{memCap}",
                Disk = $"{diskAlloc}/{diskCap}",
                Pods = podsOnNode,
            });
        }

        // ── MSSQL pod status ────────────────────────────────────────────────────
        object? mssqlStatus = null;
        try
        {
            var mssqlPods = await client.ListNamespacedPodAsync(Namespace, labelSelector: "app=mssql");
            var mssqlPod = mssqlPods.Items.FirstOrDefault(p => p.Status?.Phase == "Running");
            if (mssqlPod != null)
            {
                // Get SQL data drive usage via exec
                string? sqlDiskUsage = null;
                try
                {
                    var result = await ExecInMssqlPodAsync(client, mssqlPod.Metadata.Name,
                        "df -h /var/opt/mssql 2>/dev/null | tail -1 | awk '{print \"used: \" $3 \" / \" $2 \" (\" $5 \" full)\"}'");
                    sqlDiskUsage = result.Stdout.Trim();
                    if (string.IsNullOrWhiteSpace(sqlDiskUsage)) sqlDiskUsage = null;
                }
                catch { /* exec may fail */ }

                // Get pod memory metrics
                string? memoryUsage = null;
                try
                {
                    var metrics = await client.GetNamespacedCustomObjectAsync<PodMetricsResult>(
                        "metrics.k8s.io", "v1beta1", Namespace, "pods", mssqlPod.Metadata.Name);
                    var container = metrics?.Containers?.FirstOrDefault();
                    if (container?.Usage != null)
                    {
                        var memVal = container.Usage.TryGetValue("memory", out var m) ? FormatBytes(m.ToDouble()) : null;
                        var cpuVal = container.Usage.TryGetValue("cpu", out var c) ? c.ToString() : null;
                        memoryUsage = memVal != null ? $"{memVal} memory, {cpuVal} cpu" : null;
                    }
                }
                catch { /* metrics may not be available */ }

                mssqlStatus = new
                {
                    Pod = mssqlPod.Metadata.Name,
                    Node = mssqlPod.Spec.NodeName,
                    Phase = mssqlPod.Status.Phase,
                    SqlDataDrive = sqlDiskUsage,
                    ResourceUsage = memoryUsage,
                };
            }
        }
        catch { /* mssql pod may not exist */ }

        // ── Windows VMs and BC containers ───────────────────────────────────────
        var windowsNodes = nodes.Items
            .Where(n => n.Metadata.Labels.TryGetValue("kubernetes.io/os", out var os) && os == "windows")
            .OrderBy(n => n.Metadata.Name)
            .ToList();

        // CNS status
        var kubeSystemPods = await client.ListNamespacedPodAsync("kube-system");
        var cnsPods = kubeSystemPods.Items
            .Where(p => p.Metadata.Name.StartsWith("azure-cns", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // BC deployments (windows-servicetier)
        var bcDeployments = allDeployments.Items
            .Where(d => d.Spec.Template.Metadata.Labels != null
                && d.Spec.Template.Metadata.Labels.TryGetValue("app-type", out var appType)
                && appType == "windows-servicetier")
            .ToList();

        // Pod metrics for BC pods
        var podMetrics = new Dictionary<string, PodMetricsResult>();
        try
        {
            var bcPods = await client.ListNamespacedPodAsync(Namespace, labelSelector: "app-type=windows-servicetier");
            foreach (var pod in bcPods.Items)
            {
                try
                {
                    var m = await client.GetNamespacedCustomObjectAsync<PodMetricsResult>(
                        "metrics.k8s.io", "v1beta1", Namespace, "pods", pod.Metadata.Name);
                    if (m != null && pod.Metadata.Labels.TryGetValue("app", out var appLabel))
                        podMetrics[appLabel] = m;
                }
                catch { }
            }
        }
        catch { }

        var vmResults = new List<object>();
        foreach (var node in windowsNodes)
        {
            var name = node.Metadata.Name;
            var ready = node.Status.Conditions.Any(c => c.Type == "Ready" && c.Status == "True");
            var cpuCap = node.Status.Capacity.TryGetValue("cpu", out var cpu) ? cpu.ToString() : "?";
            var memCap = node.Status.Capacity.TryGetValue("memory", out var mem) ? FormatMemory(mem.ToString()) : "?";
            var cpuAlloc = node.Status.Allocatable.TryGetValue("cpu", out var cpuA) ? cpuA.ToString() : "?";
            var memAlloc = node.Status.Allocatable.TryGetValue("memory", out var memA) ? FormatMemory(memA.ToString()) : "?";
            var diskCap = node.Status.Capacity.TryGetValue("ephemeral-storage", out var disk) ? FormatStorage(disk.ToString()) : "?";
            var diskAlloc = node.Status.Allocatable.TryGetValue("ephemeral-storage", out var diskA) ? FormatStorage(diskA.ToString()) : "?";

            var cnsReady = cnsPods.Any(p =>
                p.Spec.NodeName == name
                && p.Status.Phase == "Running"
                && p.Status.ContainerStatuses?.All(c => c.Ready) == true);

            // BC containers on this node
            var containersOnNode = bcDeployments
                .Where(d =>
                {
                    var appLabel = d.Spec.Template.Metadata.Labels.TryGetValue("app", out var app) ? app : "";
                    return appPods.Items.Any(p => p.Spec.NodeName == name
                        && p.Metadata.Labels.TryGetValue("app", out var podApp) && podApp == appLabel);
                })
                .Select(d =>
                {
                    var appLabel = d.Spec.Template.Metadata.Labels["app"];
                    var replicas = d.Spec.Replicas ?? 0;
                    var readyReplicas = d.Status?.ReadyReplicas ?? 0;
                    var status = replicas == 0 ? "Stopped" : readyReplicas >= replicas ? "Running" : "Starting";

                    string? memStr = null;
                    if (podMetrics.TryGetValue(appLabel, out var pm))
                    {
                        var c = pm.Containers?.FirstOrDefault();
                        if (c?.Usage?.TryGetValue("memory", out var mv) == true)
                            memStr = FormatBytes(mv.ToDouble());
                    }

                    return new { Name = appLabel, Status = status, Memory = memStr };
                })
                .OrderBy(c => c.Name)
                .ToList();

            vmResults.Add(new
            {
                Name = name,
                Status = ready ? "Ready" : "NotReady",
                Cns = cnsReady ? "Ready" : "NotReady",
                Cpu = $"{cpuAlloc}/{cpuCap}",
                Memory = $"{memAlloc}/{memCap}",
                Disk = $"{diskAlloc}/{diskCap}",
                ContainerCount = containersOnNode.Count,
                Containers = containersOnNode,
            });
        }

        // Summary stats
        var totalBcContainers = bcDeployments.Count;
        var runningBcContainers = bcDeployments.Count(d => (d.Spec.Replicas ?? 0) > 0 && (d.Status?.ReadyReplicas ?? 0) > 0);
        var stoppedBcContainers = bcDeployments.Count(d => (d.Spec.Replicas ?? 0) == 0);

        return new
        {
            LinuxNodes = linuxStatus,
            Mssql = mssqlStatus,
            WindowsVMs = vmResults,
            Summary = new
            {
                WindowsVMCount = windowsNodes.Count,
                WindowsVMReady = windowsNodes.Count(n => n.Status.Conditions.Any(c => c.Type == "Ready" && c.Status == "True")),
                TotalBcContainers = totalBcContainers,
                RunningBcContainers = runningBcContainers,
                StoppedBcContainers = stoppedBcContainers,
            }
        };
    }

    private async Task<object> GetStorageStatusAsync(Azure.Core.TokenCredential credential)
    {
        var results = new List<object>();

        try
        {
            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{DbsStorageAccountName}.blob.core.windows.net"), credential);

            var accountProps = await blobServiceClient.GetPropertiesAsync();

            var containers = new List<object>();
            await foreach (var container in blobServiceClient.GetBlobContainersAsync())
            {
                long totalSize = 0;
                int blobCount = 0;
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(container.Name);
                await foreach (var blob in blobContainerClient.GetBlobsAsync())
                {
                    totalSize += blob.Properties.ContentLength ?? 0;
                    blobCount++;
                }

                containers.Add(new
                {
                    Name = container.Name,
                    BlobCount = blobCount,
                    TotalSize = FormatBytes(totalSize),
                });
            }

            results.Add(new
            {
                AccountName = DbsStorageAccountName,
                Containers = containers,
            });
        }
        catch (Exception ex)
        {
            results.Add(new
            {
                AccountName = DbsStorageAccountName,
                Error = ex.Message,
            });
        }

        return results;
    }

    private async Task<object> GetSubscriptionQuotaAsync(Azure.Core.TokenCredential credential)
    {
        try
        {
            var armClient = new ArmClient(credential);
            var subscription = armClient.GetDefaultSubscription();

            // Get AKS cluster to check agent pool profiles
            var aksId = ContainerServiceManagedClusterResource
                .CreateResourceIdentifier(SubscriptionId, ResourceGroup, ClusterName);
            var cluster = armClient.GetContainerServiceManagedClusterResource(aksId);
            var clusterData = (await cluster.GetAsync()).Value.Data;

            var agentPools = clusterData.AgentPoolProfiles?.Select(p => new
            {
                Name = p.Name,
                VmSize = p.VmSize,
                Count = p.Count,
                MinCount = p.MinCount,
                MaxCount = p.MaxCount,
                Mode = p.Mode?.ToString(),
                OsSku = p.OSSku?.ToString(),
                EnableAutoScaling = p.IsAutoScalingEnabled,
            }).ToList();

            return new
            {
                SubscriptionId,
                ResourceGroup,
                ClusterName,
                AgentPools = agentPools,
            };
        }
        catch (Exception ex)
        {
            return new { Error = ex.Message };
        }
    }

    private static object GetSecurityStatus()
    {
        // Access the static FailedAttempts from FunctionBase
        var blockedIps = FunctionBase.GetBlockedIps();
        var recentFailures = FunctionBase.GetRecentFailedAttempts();

        return new
        {
            BlockedIpCount = blockedIps.Count,
            BlockedIps = blockedIps,
            RecentFailedAttemptCount = recentFailures.Count,
            RecentFailedAttempts = recentFailures,
        };
    }

    // ── Metrics model (shared with status) ──────────────────────────────────────
    private class PodMetricsResult
    {
        public List<ContainerMetricsResult>? Containers { get; set; }
    }

    private class ContainerMetricsResult
    {
        public string? Name { get; set; }
        public Dictionary<string, ResourceQuantity>? Usage { get; set; }
    }

    private static string FormatMemory(string memKi)
    {
        if (memKi.EndsWith("Ki") && long.TryParse(memKi[..^2], out var ki))
        {
            var gi = ki / (1024.0 * 1024.0);
            return $"{gi:F1}Gi";
        }
        return memKi;
    }

    private static string FormatStorage(string value)
    {
        if (value.EndsWith("Ki") && long.TryParse(value[..^2], out var ki))
        {
            var gi = ki / (1024.0 * 1024.0);
            return $"{gi:F1}Gi";
        }
        return value;
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1}Gi";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F0}Mi";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F0}Ki";
        return $"{bytes:F0}B";
    }
}
