using k8s;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fkh.Services;

public class FkhGetAppInfo : FkhServiceBase
{
    public FkhGetAppInfo(ILogger<FkhGetAppInfo> logger) : base(logger) { }

    public async Task<object> GetAppInfoAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var appName = ResolveAppName(parameters);
        var tenant = parameters.TryGetValue("tenant", out var t) ? t : "default";
        var filterAppName = parameters.TryGetValue("appName", out var an) ? an : null;
        var filterAppPublisher = parameters.TryGetValue("appPublisher", out var ap) ? ap : null;
        var filterAppId = parameters.TryGetValue("appId", out var ai) ? ai : null;

        Logger.LogInformation(
            "User '{User}' getting app info from container '{Container}' (tenant={Tenant}).",
            githubUsername, appName, tenant);

        var client = await GetKubernetesClientAsync();

        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={appName}");
        var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
            ?? throw new InvalidOperationException($"No running container found for '{appName}'. Make sure the container is started and ready.");

        var podName = pod.Metadata.Name;
        var containerName = pod.Spec.Containers[0].Name;

        var script = $@"
$ErrorActionPreference = 'Stop'
. 'c:\run\prompt.ps1'
Get-NAVAppInfo -ServerInstance bc -TenantSpecificProperties -Tenant '{tenant}' |
    Select-Object AppId, Name, Publisher, Version, ExtensionType, Scope, IsInstalled, IsPublished, SyncState, NeedsUpgrade |
    ConvertTo-Json -Depth 5
";

        var result = await ExecInBcPodAsync(client, podName, containerName, script);

        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            throw new InvalidOperationException($"Failed to get app info from container '{appName}':\n{result.Stderr.TrimEnd()}");
        }

        // Parse the JSON output
        var jsonStart = result.Stdout.IndexOf('[');
        var jsonStartObj = result.Stdout.IndexOf('{');
        if (jsonStart < 0 || (jsonStartObj >= 0 && jsonStartObj < jsonStart))
            jsonStart = jsonStartObj;

        if (jsonStart < 0)
        {
            return new
            {
                Container = appName,
                Tenant = tenant,
                Apps = Array.Empty<object>(),
                Message = "No apps found."
            };
        }

        var jsonText = result.Stdout[jsonStart..].TrimEnd();
        using var doc = JsonDocument.Parse(jsonText);
        var apps = new List<JsonElement>();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
                apps.Add(item);
        }
        else
        {
            apps.Add(doc.RootElement);
        }

        // Apply client-side filters
        if (!string.IsNullOrEmpty(filterAppId))
        {
            apps = apps.Where(a =>
                a.TryGetProperty("AppId", out var id) &&
                string.Equals(id.GetString(), filterAppId, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrEmpty(filterAppName))
        {
            var pattern = WildcardToRegex(filterAppName);
            apps = apps.Where(a =>
                a.TryGetProperty("Name", out var n) &&
                Regex.IsMatch(n.GetString() ?? "", pattern, RegexOptions.IgnoreCase)).ToList();
        }
        if (!string.IsNullOrEmpty(filterAppPublisher))
        {
            var pattern = WildcardToRegex(filterAppPublisher);
            apps = apps.Where(a =>
                a.TryGetProperty("Publisher", out var p) &&
                Regex.IsMatch(p.GetString() ?? "", pattern, RegexOptions.IgnoreCase)).ToList();
        }

        return new
        {
            Container = appName,
            Tenant = tenant,
            Apps = apps.Select(a => JsonSerializer.Deserialize<object>(a.GetRawText())).ToArray()
        };
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
    }

    private async Task<ExecResult> ExecInBcPodAsync(Kubernetes client, string podName, string containerName, string psScript)
    {
        var command = new[] { "powershell", "-NoProfile", "-Command", psScript };
        var ws = await client.WebSocketNamespacedPodExecAsync(
            podName, Namespace, command, containerName,
            stderr: true, stdin: false, stdout: true, tty: false);

        using var demux = new k8s.StreamDemuxer(ws);
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
            Logger.LogWarning("BC pod exec stderr: {StdErr}", stderr);
        }

        return new ExecResult(stdoutTask.Result, stderr);
    }
}
