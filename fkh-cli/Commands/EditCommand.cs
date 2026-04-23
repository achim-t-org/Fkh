using System.Diagnostics;

sealed class EditCommand : ClientCommand
{
    public override string Name => "Edit";
    public override string Description => "Copies a file from a container, opens it in an editor, and copies it back when the editor closes.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "name", Type = "string", Description = "Name of the container.", Required = true },
        new() { Name = "containerFilename", Type = "string", Description = "Path to the file inside the container (supports wildcards).", Required = true },
        new() { Name = "wait", Type = "flag", Description = "Block the process until the editor closes (default opens a new window).", Required = false }
    ];

    public override Task<int> ExecuteAsync(string[] args, CliSettings settings, bool asJson)
    {
        Dictionary<string, string> parameters;
        try
        {
            parameters = ParseClientArgs(args);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"{Ansi.Red}{ex.Message}{Ansi.Reset}");
            return Task.FromResult(1);
        }

        if (!parameters.TryGetValue("name", out var containerName) || string.IsNullOrWhiteSpace(containerName))
        {
            Console.Error.WriteLine($"{Ansi.Red}Missing required parameter --name{Ansi.Reset}");
            return Task.FromResult(1);
        }

        if (!parameters.TryGetValue("containerFilename", out var filename) || string.IsNullOrWhiteSpace(filename))
        {
            Console.Error.WriteLine($"{Ansi.Red}Missing required parameter --containerFilename{Ansi.Reset}");
            return Task.FromResult(1);
        }

        // Default: re-invoke fkh edit in a new window (with --wait) so the current process returns immediately
        if (!args.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase)))
        {
            var exePath = Environment.ProcessPath ?? "fkh";
            Console.WriteLine($"{Ansi.Cyan}Launching edit in a new window...{Ansi.Reset}");
            var reArgs = args.Concat(new[] { "--wait" });
            if (!LaunchInNewTerminal(exePath, reArgs))
            {
                Console.Error.WriteLine($"{Ansi.Yellow}Could not open a new terminal window — running inline.{Ansi.Reset}");
                // Fall through to inline execution below
            }
            else
            {
                return Task.FromResult(0);
            }
        }

        // Resolve container name to pod (same as server-side ResolveAppName)
        // If name contains '-', treat as full name (admin); otherwise prefix with GitHub username
        var appName = containerName.Contains('-')
            ? SanitizeAppName(containerName)
            : SanitizeAppName($"{GetGitHubUsername()}-{containerName}");

        Console.WriteLine($"{Ansi.Dim}Looking up pod for container '{containerName}' (app={appName})...{Ansi.Reset}");

        var podName = RunKubectl("get", "pods", "-n", "app", "-l", $"app={appName}", "-o", "jsonpath={.items[0].metadata.name}");
        if (string.IsNullOrWhiteSpace(podName))
        {
            Console.Error.WriteLine($"{Ansi.Red}No pod found for container '{containerName}' (app={appName}).{Ansi.Reset}");
            return Task.FromResult(1);
        }

        // If filename contains wildcards, resolve the actual path inside the container
        if (filename.Contains('*'))
        {
            Console.WriteLine($"{Ansi.Dim}Resolving wildcard path: {filename}{Ansi.Reset}");
            var resolved = RunKubectl("exec", podName, "-n", "app", "--", "pwsh", "-c",
                $"(Resolve-Path '{filename}' -ErrorAction Stop).Path");
            if (string.IsNullOrWhiteSpace(resolved))
            {
                Console.Error.WriteLine($"{Ansi.Red}No file matched: {filename}{Ansi.Reset}");
                return Task.FromResult(1);
            }
            filename = resolved.Trim().Split('\n')[0].Trim();
            Console.WriteLine($"{Ansi.Dim}Resolved to: {filename}{Ansi.Reset}");
        }

        // Create a temp file locally (use relative path — kubectl cp can't handle C: in both src and dest)
        var tempFileName = $"fkh-edit-{Path.GetFileName(filename)}";
        var tempDir = Path.GetTempPath();
        var localFile = Path.Combine(tempDir, tempFileName);

        // Download file content via kubectl exec + PowerShell streaming
        Console.WriteLine($"{Ansi.Dim}Downloading {filename} from {podName}...{Ansi.Reset}");
        var downloadOk = DownloadFileFromPod(podName, filename, localFile);
        if (!downloadOk)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to download file from pod.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        if (!File.Exists(localFile))
        {
            Console.Error.WriteLine($"{Ansi.Red}File was not downloaded. Check that the path exists in the container.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        var beforeHash = GetFileHash(localFile);

        // Open in editor and wait for it to close
        var editor = GetEditorCommand();
        Console.WriteLine($"{Ansi.Cyan}Opening in {Path.GetFileNameWithoutExtension(editor)} — save and close to upload changes...{Ansi.Reset}");
        var editorProcess = Process.Start(new ProcessStartInfo
        {
            FileName = editor,
            ArgumentList = { localFile },
            UseShellExecute = false,
        });
        if (editorProcess is null)
        {
            Console.Error.WriteLine($"{Ansi.Red}Could not start editor ({editor}).{Ansi.Reset}");
            return Task.FromResult(1);
        }
        editorProcess.WaitForExit();

        // Check if file was modified
        var afterHash = GetFileHash(localFile);
        if (beforeHash == afterHash)
        {
            Console.WriteLine($"{Ansi.Yellow}No changes detected — skipping upload.{Ansi.Reset}");
            File.Delete(localFile);
            return Task.FromResult(0);
        }

        // Upload back to pod via kubectl exec + stdin streaming
        Console.WriteLine($"{Ansi.Dim}Uploading changes to {filename} in {podName}...{Ansi.Reset}");
        var uploadOk = UploadFileToPod(podName, filename, localFile);
        if (!uploadOk)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to upload file back to pod.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        Console.WriteLine($"{Ansi.Cyan}File updated in container.{Ansi.Reset}");
        File.Delete(localFile);
        return Task.FromResult(0);
    }

    private static string GetFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
