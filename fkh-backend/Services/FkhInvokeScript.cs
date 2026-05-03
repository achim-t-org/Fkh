using k8s;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Fkh.Services;

public class FkhInvokeScript : FkhServiceBase
{
    public FkhInvokeScript(ILogger<FkhInvokeScript> logger) : base(logger) { }

    /// <summary>
    /// Invokes a script when called with --command (no file upload, regular JSON body).
    /// </summary>
    public async Task<object> InvokeScriptAsync(Dictionary<string, string> parameters)
    {
        var script = parameters.TryGetValue("command", out var cmd) ? cmd : null;
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new InvalidOperationException("Either --command or --file must be provided.");
        }

        return await RunScriptInContainerAsync(parameters, script);
    }

    /// <summary>
    /// Invokes a script when called with --file (multipart upload).
    /// Also handles --command when sent as multipart (files dict will be empty).
    /// </summary>
    public async Task<object> InvokeScriptWithFileAsync(Dictionary<string, string> parameters, Dictionary<string, byte[]> files)
    {
        string script;

        if (files.TryGetValue("scriptFile", out var fileBytes) && fileBytes.Length > 0)
        {
            script = Encoding.UTF8.GetString(fileBytes);
        }
        else if (parameters.TryGetValue("command", out var cmd) && !string.IsNullOrWhiteSpace(cmd))
        {
            script = cmd;
        }
        else
        {
            throw new InvalidOperationException("Either --command or --file must be provided.");
        }

        return await RunScriptInContainerAsync(parameters, script);
    }

    private async Task<object> RunScriptInContainerAsync(Dictionary<string, string> parameters, string script)
    {
        var githubUsername = parameters["_githubUsername"];
        var appName = ResolveAppName(parameters);
        var scriptParams = parameters.TryGetValue("scriptParams", out var sp) ? sp : "";

        Logger.LogInformation(
            "User '{User}' invoking script in container '{Container}'.",
            githubUsername, appName);

        var client = await GetKubernetesClientAsync();

        // Find the BC pod for this container
        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={appName}");
        var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
            ?? throw new InvalidOperationException($"No running container found for '{appName}'. Make sure the container is started and ready.");

        var podName = pod.Metadata.Name;
        var containerName = pod.Spec.Containers[0].Name;

        // Write script to a temp file in the container and invoke it.
        // This ensures param() blocks work regardless of --command vs --scriptFile.
        var tempFileName = $"fkh-{Guid.NewGuid():N}.ps1";
        var tempFilePath = $"C:\\run\\my\\{tempFileName}";

        // Base64-encode the script to safely transfer it into the container
        var scriptBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var writeScript = $"[IO.File]::WriteAllBytes('{tempFilePath}', [Convert]::FromBase64String('{scriptBase64}'))";
        await ExecInBcPodPwshAsync(client, podName, containerName, writeScript);

        try
        {
            var fullScript = $". 'C:\\run\\prompt.ps1' -silent; . '{tempFilePath}' {scriptParams}";
            var result = await ExecInBcPodPwshAsync(client, podName, containerName, fullScript);

            return new
            {
                Container = appName,
                Output = result.Stdout.TrimEnd(),
                Stderr = string.IsNullOrWhiteSpace(result.Stderr) ? null : result.Stderr.TrimEnd(),
            };
        }
        finally
        {
            // Clean up the temp file
            try
            {
                await ExecInBcPodPwshAsync(client, podName, containerName, $"Remove-Item '{tempFilePath}' -Force -ErrorAction SilentlyContinue");
            }
            catch { /* best-effort cleanup */ }
        }
    }

    private async Task<ExecResult> ExecInBcPodPwshAsync(Kubernetes client, string podName, string containerName, string psScript)
    {
        var command = new[] { "pwsh", "-NoProfile", "-Command", psScript };
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
            Logger.LogWarning("BC pod pwsh exec stderr: {StdErr}", stderr);
        }

        return new ExecResult(stdoutTask.Result, stderr);
    }
}
