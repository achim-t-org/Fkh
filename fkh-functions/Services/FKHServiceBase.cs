using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using FKH.Models;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace FKH.Services;

public abstract class FKHServiceBase
{
    protected readonly string ResourceGroup;
    protected readonly string ClusterName;
    protected readonly string SubscriptionId;
    protected readonly string AcrName;
    protected readonly string ClientId;
    protected readonly string BaseImage;
    protected readonly string AksLocation;
    protected readonly string ContactEmail;
    protected readonly string DbsStorageAccountName;
    protected readonly string? LogAnalyticsWorkspaceId;
    protected readonly ILogger Logger;

    protected const string Namespace = "app";
    protected const string AcrRepository = "businesscentral";
    protected const string FoldersValue = @"c:\run\my=https://github.com/Freddy-DK/ContainerScripts/archive/refs/heads/main.zip\ContainerScripts-main";

    protected FKHServiceBase(ILogger logger)
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
        DbsStorageAccountName = Environment.GetEnvironmentVariable("DBS_STORAGE_ACCOUNT_NAME")
            ?? throw new InvalidOperationException("DBS_STORAGE_ACCOUNT_NAME is not configured.");
        LogAnalyticsWorkspaceId = Environment.GetEnvironmentVariable("LOG_ANALYTICS_WORKSPACE_ID");
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

        // Azure Functions sandbox blocks writes to the Windows certificate store.
        // Use EphemeralKeySet so the PFX stays in memory.
        if (config.SslCaCerts != null)
        {
            var updated = new X509Certificate2Collection();
            foreach (var cert in config.SslCaCerts)
            {
                updated.Add(new X509Certificate2(cert.RawData, (string?)null, X509KeyStorageFlags.EphemeralKeySet));
            }
            config.SslCaCerts = updated;
        }
        if (config.ClientCertificateData != null)
        {
            var certBytes = Convert.FromBase64String(config.ClientCertificateData);
            var keyBytes = config.ClientCertificateKeyData != null
                ? Convert.FromBase64String(config.ClientCertificateKeyData)
                : null;

            if (keyBytes != null)
            {
                var certPem = System.Text.Encoding.UTF8.GetString(certBytes);
                var keyPem = System.Text.Encoding.UTF8.GetString(keyBytes);
                config.ClientCertificateData = null;
                config.ClientCertificateKeyData = null;
                var clientCert = X509Certificate2.CreateFromPem(certPem, keyPem);
                // Export/reimport with EphemeralKeySet to avoid certificate store writes
                config.ClientCertificateData = Convert.ToBase64String(
                    clientCert.Export(X509ContentType.Pfx));
            }
        }

        return new Kubernetes(config);
    }

    /// <summary>
    /// Ensures at least one Windows node is Ready and has a running CNS pod.
    /// If no Windows node exists, triggers the cluster autoscaler by creating a
    /// temporary placeholder pod, then throws <see cref="RetryAfterException"/>
    /// so the caller retries after the node is provisioned.
    /// </summary>
    protected async Task EnsureWindowsNodeReadyAsync(Kubernetes client)
    {
        // Check for Ready Windows nodes
        var nodes = await client.ListNodeAsync();
        var windowsNodes = nodes.Items
            .Where(n => n.Metadata.Labels.TryGetValue("kubernetes.io/os", out var os) && os == "windows")
            .ToList();

        var readyWindowsNodes = windowsNodes
            .Where(n => n.Status.Conditions.Any(c => c.Type == "Ready" && c.Status == "True"))
            .ToList();

        if (readyWindowsNodes.Count == 0)
        {
            Logger.LogInformation("No Ready Windows nodes found. Triggering autoscaler...");
            await CreatePlaceholderPodAsync(client);
            throw new RetryAfterException(
                "No Windows node is available. The cluster autoscaler is provisioning one. Please retry in a few minutes.",
                retryAfterSeconds: 120);
        }

        // Check that CNS is running on at least one Ready Windows node
        var cnsPods = await client.ListNamespacedPodAsync("kube-system",
            labelSelector: "k8s-app=azure-cns");
        var readyNodeNames = readyWindowsNodes.Select(n => n.Metadata.Name).ToHashSet();
        var cnsOnWindows = cnsPods.Items
            .Where(p => p.Spec.NodeName != null
                && readyNodeNames.Contains(p.Spec.NodeName)
                && p.Status.Phase == "Running"
                && p.Status.ContainerStatuses?.All(c => c.Ready) == true)
            .ToList();

        if (cnsOnWindows.Count == 0)
        {
            Logger.LogInformation("Windows nodes exist but CNS is not ready yet.");
            throw new RetryAfterException(
                "A Windows node is starting up but networking is not ready yet. Please retry in a couple of minutes.",
                retryAfterSeconds: 60);
        }

        Logger.LogInformation("Windows node ready with healthy CNS on {NodeCount} node(s).", cnsOnWindows.Count);
    }

    private async Task CreatePlaceholderPodAsync(Kubernetes client)
    {
        const string podName = "fkh-windows-warmup";

        // Check if placeholder already exists
        try
        {
            await client.ReadNamespacedPodAsync(podName, Namespace);
            Logger.LogInformation("Warmup placeholder pod already exists.");
            return;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Expected — create it
        }

        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = podName,
                NamespaceProperty = Namespace,
                Labels = new Dictionary<string, string> { ["fkh/purpose"] = "warmup" }
            },
            Spec = new V1PodSpec
            {
                NodeSelector = new Dictionary<string, string> { ["kubernetes.io/os"] = "windows" },
                Containers = new List<V1Container>
                {
                    new()
                    {
                        Name = "warmup",
                        Image = "mcr.microsoft.com/windows/nanoserver:ltsc2022",
                        Command = new List<string> { "cmd", "/c", "echo ready && ping -n 600 127.0.0.1 > nul" },
                        Resources = new V1ResourceRequirements
                        {
                            Requests = new Dictionary<string, ResourceQuantity>
                            {
                                ["cpu"] = new ResourceQuantity("100m"),
                                ["memory"] = new ResourceQuantity("128Mi")
                            }
                        }
                    }
                },
                RestartPolicy = "Never"
            }
        };

        await client.CreateNamespacedPodAsync(pod, Namespace);
        Logger.LogInformation("Created warmup placeholder pod to trigger Windows node autoscaling.");
    }

    /// <summary>
    /// Cleans up the warmup placeholder pod if it exists.
    /// Call after a Windows node is confirmed ready.
    /// </summary>
    protected async Task CleanupPlaceholderPodAsync(Kubernetes client)
    {
        try
        {
            await client.DeleteNamespacedPodAsync("fkh-windows-warmup", Namespace);
            Logger.LogInformation("Cleaned up warmup placeholder pod.");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone
        }
    }

    protected static string GetImageTag(string artifactUrl)
    {
        var uri = new Uri(artifactUrl);
        return uri.AbsolutePath.Replace('/', '-').TrimStart('-').ToLowerInvariant();
    }

    protected static string SanitizeAppName(string name)
    {
        var appName = name.Replace('.', '-').Replace('_', '-').ToLowerInvariant();
        if (appName.Length > 63) appName = appName[..63];
        return appName.TrimEnd('-');
    }

    protected const string SqlcmdPath = "/opt/mssql-tools18/bin/sqlcmd";

    protected async Task<string> FindMssqlPodAsync(Kubernetes client)
    {
        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: "app=mssql");
        var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
            ?? throw new InvalidOperationException($"No running MSSQL pod found in namespace '{Namespace}'.");
        return pod.Metadata.Name;
    }

    protected record ExecResult(string Stdout, string Stderr)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Stderr) ? Stdout : $"{Stdout}\n[stderr]: {Stderr}";
    }

    protected async Task<ExecResult> ExecInMssqlPodAsync(Kubernetes client, string podName, string bashScript)
    {
        var command = new[] { "/bin/bash", "-c", bashScript };
        var ws = await client.WebSocketNamespacedPodExecAsync(
            podName, Namespace, command, "mssql",
            stderr: true, stdin: false, stdout: true, tty: false);

        using var demux = new StreamDemuxer(ws);
        demux.Start();

        var stdoutStream = demux.GetStream(1, null);
        var stderrStream = demux.GetStream(2, null);

        using var stdoutReader = new StreamReader(stdoutStream);
        using var stderrReader = new StreamReader(stderrStream);

        var stdoutTask = stdoutReader.ReadToEndAsync();
        var stderrTask = stderrReader.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);

        var stderr = stderrTask.Result;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Logger.LogWarning("MSSQL pod exec stderr: {StdErr}", stderr);
        }

        return new ExecResult(stdoutTask.Result, stderr);
    }

    protected const string AutoStopAnnotation = "fkh/auto-stop-at";

    protected async Task SetAutoStopAnnotationAsync(Kubernetes client, string deploymentName, DateTimeOffset stopAt)
    {
        var deployment = await client.ReadNamespacedDeploymentAsync(deploymentName, Namespace);
        deployment.Metadata.Annotations ??= new Dictionary<string, string>();
        deployment.Metadata.Annotations[AutoStopAnnotation] = stopAt.UtcDateTime.ToString("o");
        await client.ReplaceNamespacedDeploymentAsync(deployment, deploymentName, Namespace);
        Logger.LogInformation("Set auto-stop annotation on '{Deployment}' to {StopAt}", deploymentName, stopAt);
    }

    protected async Task ClearAutoStopAnnotationAsync(Kubernetes client, string deploymentName)
    {
        var deployment = await client.ReadNamespacedDeploymentAsync(deploymentName, Namespace);
        if (deployment.Metadata.Annotations?.Remove(AutoStopAnnotation) == true)
        {
            await client.ReplaceNamespacedDeploymentAsync(deployment, deploymentName, Namespace);
            Logger.LogInformation("Cleared auto-stop annotation on '{Deployment}'.", deploymentName);
        }
    }
}
