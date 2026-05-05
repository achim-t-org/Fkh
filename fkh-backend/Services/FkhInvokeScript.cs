using Fkh.Models;
using k8s;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
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

        // Deterministic job ID so retries find the same running job
        var jobId = ComputeJobId(appName, script, scriptParams);
        var basePath = $"C:\\run\\my\\fkh-{jobId}";
        var scriptPath = $"{basePath}.ps1";
        var wrapperPath = $"{basePath}-run.ps1";
        var stdoutPath = $"{basePath}.stdout";
        var stderrPath = $"{basePath}.stderr";
        var donePath = $"{basePath}.done";

        // Check if job is already complete (retry after previous timeout)
        var doneCheck = await ExecInBcPodPwshAsync(client, podName, containerName,
            $"if (Test-Path '{donePath}') {{ 'DONE' }} else {{ 'PENDING' }}");

        if (doneCheck.Stdout.Trim() == "DONE")
        {
            return await CollectResultAndCleanupAsync(client, podName, containerName, appName, basePath, stdoutPath, stderrPath);
        }

        // Check if job is already running (script file exists but no done marker)
        var runningCheck = await ExecInBcPodPwshAsync(client, podName, containerName,
            $"if (Test-Path '{scriptPath}') {{ 'RUNNING' }} else {{ 'NEW' }}");

        if (runningCheck.Stdout.Trim() == "NEW")
        {
            // First invocation — write script and wrapper, launch detached
            var scriptBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            await ExecInBcPodPwshAsync(client, podName, containerName,
                $"[IO.File]::WriteAllBytes('{scriptPath}', [Convert]::FromBase64String('{scriptBase64}'))");

            var wrapperScript = $@"
try {{
    . 'C:\run\my\prompt.ps1' -silent
    & {{ . '{scriptPath}' {scriptParams} }} 2> '{stderrPath}' 6>&1 3>&1 4>&1 5>&1 | Out-File '{stdoutPath}' -Encoding utf8
}} catch {{
    $_.Exception.Message | Out-File '{stderrPath}' -Append -Encoding utf8
}} finally {{
    'DONE' | Out-File '{donePath}' -NoNewline
}}";
            var wrapperBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(wrapperScript));
            await ExecInBcPodPwshAsync(client, podName, containerName,
                $"[IO.File]::WriteAllBytes('{wrapperPath}', [Convert]::FromBase64String('{wrapperBase64}'))");

            // Launch detached — output redirection is handled inside the wrapper script
            await ExecInBcPodPwshAsync(client, podName, containerName,
                $"Start-Process -FilePath 'pwsh' -ArgumentList '-NoProfile','-File','{wrapperPath}' -WindowStyle Hidden");
        }

        // Wait up to 30 seconds for the script to finish before returning 202
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(5_000);

            var pollCheck = await ExecInBcPodPwshAsync(client, podName, containerName,
                $"if (Test-Path '{donePath}') {{ 'DONE' }} else {{ 'PENDING' }}");

            if (pollCheck.Stdout.Trim() == "DONE")
            {
                return await CollectResultAndCleanupAsync(client, podName, containerName, appName, basePath, stdoutPath, stderrPath);
            }
        }

        // Script still running — tell client to poll back
        throw new RetryAfterException("Script still running...", 5);
    }

    private async Task<object> CollectResultAndCleanupAsync(
        Kubernetes client, string podName, string containerName, string appName,
        string basePath, string stdoutPath, string stderrPath)
    {
        var stdoutResult = await ExecInBcPodPwshAsync(client, podName, containerName,
            $"if (Test-Path '{stdoutPath}') {{ Get-Content '{stdoutPath}' -Raw }} else {{ '' }}");
        var stderrResult = await ExecInBcPodPwshAsync(client, podName, containerName,
            $"if (Test-Path '{stderrPath}') {{ Get-Content '{stderrPath}' -Raw }} else {{ '' }}");

        // Clean up all job files
        try
        {
            await ExecInBcPodPwshAsync(client, podName, containerName,
                $"Remove-Item '{basePath}*' -Force -ErrorAction SilentlyContinue");
        }
        catch { /* best-effort cleanup */ }

        var stdout = stdoutResult.Stdout.TrimEnd();
        var stderr = stderrResult.Stdout.TrimEnd();

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            var message = string.IsNullOrWhiteSpace(stdout)
                ? $"Script failed in container '{appName}':\n{stderr}"
                : $"Script failed in container '{appName}':\n{stderr}\n\nOutput:\n{stdout}";
            throw new InvalidOperationException(message);
        }

        return new
        {
            Container = appName,
            Output = stdout,
        };
    }

    private static string ComputeJobId(string appName, string script, string scriptParams)
    {
        var input = $"{appName}|{script}|{scriptParams}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
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
