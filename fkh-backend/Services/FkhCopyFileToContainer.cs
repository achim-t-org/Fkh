using k8s;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhCopyFileToContainer : FkhServiceBase
{
    public FkhCopyFileToContainer(ILogger<FkhCopyFileToContainer> logger) : base(logger) { }

    public async Task<object> CopyFileToContainerAsync(Dictionary<string, string> parameters, Dictionary<string, byte[]> files)
    {
        var githubUsername = parameters["_githubUsername"];
        var appName = ResolveAppName(parameters);

        if (!parameters.TryGetValue("containerFilename", out var destPath) || string.IsNullOrWhiteSpace(destPath))
            throw new InvalidOperationException("Missing required parameter 'containerFilename'.");

        if (!files.TryGetValue("file", out var fileBytes) || fileBytes.Length == 0)
            throw new InvalidOperationException("No file was uploaded.");

        Logger.LogInformation(
            "User '{User}' uploading file to '{Dest}' in container '{Container}' ({Size} bytes).",
            githubUsername, destPath, appName, fileBytes.Length);

        var client = await GetKubernetesClientAsync();

        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={appName}");
        var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
            ?? throw new InvalidOperationException($"No running container found for '{appName}'. Make sure the container is started and ready.");

        var podName = pod.Metadata.Name;
        var containerName = pod.Spec.Containers[0].Name;

        await CopyFileToPodAsync(client, podName, containerName, fileBytes, destPath);

        Logger.LogInformation("File uploaded to '{Dest}' in container '{Container}'.", destPath, appName);

        return new
        {
            Message = "File copied to container.",
            Container = appName,
            DestinationPath = destPath,
            Size = fileBytes.Length,
        };
    }

    private async Task CopyFileToPodAsync(Kubernetes client, string podName, string containerName, byte[] fileData, string destPath)
    {
        var destDir = Path.GetDirectoryName(destPath)?.Replace('/', '\\') ?? "";
        var base64 = Convert.ToBase64String(fileData);
        const int chunkSize = 65536; // 64KB base64 chunks
        var tempPath = destPath + ".fkh-tmp";

        // Initialize: create dest directory and empty temp file (original stays untouched)
        var initScript = $@"
if (-not (Test-Path '{destDir}')) {{ New-Item -ItemType Directory -Path '{destDir}' -Force | Out-Null }}
if (Test-Path '{tempPath}') {{ Remove-Item '{tempPath}' -Force }}
[System.IO.File]::WriteAllBytes('{tempPath}', @())
Write-Host 'INIT_OK'
";
        var initResult = await ExecInPodPwshAsync(client, podName, containerName, initScript);
        if (!initResult.Stdout.Contains("INIT_OK"))
            throw new InvalidOperationException($"Failed to initialize destination: {initResult}");

        // Write base64 chunks to temp file
        for (var offset = 0; offset < base64.Length; offset += chunkSize)
        {
            var chunk = base64.Substring(offset, Math.Min(chunkSize, base64.Length - offset));
            var appendScript = $@"
$chunk = '{chunk}'
$bytes = [System.Convert]::FromBase64String($chunk)
$fs = [System.IO.File]::Open('{tempPath}', [System.IO.FileMode]::Append)
$fs.Write($bytes, 0, $bytes.Length)
$fs.Close()
Write-Host 'CHUNK_OK'
";
            var appendResult = await ExecInPodPwshAsync(client, podName, containerName, appendScript);
            if (!appendResult.Stdout.Contains("CHUNK_OK"))
            {
                // Clean up temp file on failure — original file is still intact
                await ExecInPodPwshAsync(client, podName, containerName, $"Remove-Item '{tempPath}' -Force -ErrorAction SilentlyContinue");
                throw new InvalidOperationException($"Failed to write file chunk at offset {offset}: {appendResult}");
            }
        }

        // All chunks written successfully — replace original with temp file
        var moveScript = $@"
if (Test-Path '{destPath}') {{ Remove-Item '{destPath}' -Force }}
Move-Item -Path '{tempPath}' -Destination '{destPath}' -Force
Write-Host 'MOVE_OK'
";
        var moveResult = await ExecInPodPwshAsync(client, podName, containerName, moveScript);
        if (!moveResult.Stdout.Contains("MOVE_OK"))
            throw new InvalidOperationException($"Failed to replace destination file: {moveResult}");
    }

    private async Task<ExecResult> ExecInPodPwshAsync(Kubernetes client, string podName, string containerName, string psScript)
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
