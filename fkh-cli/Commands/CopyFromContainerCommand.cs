sealed class CopyFromContainerCommand : ClientCommand
{
    public override string Name => "CopyFromContainer";
    public override string Description => "Copies a file from a container to the local machine.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "name", Type = "string", Description = "Name of the container.", Required = true },
        new() { Name = "containerFilename", Type = "string", Description = "Path to the file inside the container (supports wildcards).", Required = true },
        new() { Name = "localFilename", Type = "string", Description = "Local path to save the file to.", Required = true }
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

        if (!parameters.TryGetValue("localFilename", out var localFile) || string.IsNullOrWhiteSpace(localFile))
        {
            Console.Error.WriteLine($"{Ansi.Red}Missing required parameter --localFilename{Ansi.Reset}");
            return Task.FromResult(1);
        }

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

        Console.WriteLine($"{Ansi.Dim}Downloading {filename} from {podName}...{Ansi.Reset}");
        var ok = DownloadFileFromPod(podName, filename, localFile);
        if (!ok)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to download file from pod.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        Console.WriteLine($"{Ansi.Cyan}Saved to {Path.GetFullPath(localFile)}{Ansi.Reset}");
        return Task.FromResult(0);
    }
}
