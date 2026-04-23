sealed class CopyToContainerCommand : ClientCommand
{
    public override string Name => "CopyToContainer";
    public override string Description => "Copies a local file to a container.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "name", Type = "string", Description = "Name of the container.", Required = true },
        new() { Name = "localFilename", Type = "string", Description = "Local path of the file to upload.", Required = true },
        new() { Name = "containerFilename", Type = "string", Description = "Destination path inside the container.", Required = true }
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

        if (!parameters.TryGetValue("localFilename", out var localFile) || string.IsNullOrWhiteSpace(localFile))
        {
            Console.Error.WriteLine($"{Ansi.Red}Missing required parameter --localFilename{Ansi.Reset}");
            return Task.FromResult(1);
        }

        if (!File.Exists(localFile))
        {
            Console.Error.WriteLine($"{Ansi.Red}File not found: {localFile}{Ansi.Reset}");
            return Task.FromResult(1);
        }

        if (!parameters.TryGetValue("containerFilename", out var filename) || string.IsNullOrWhiteSpace(filename))
        {
            Console.Error.WriteLine($"{Ansi.Red}Missing required parameter --containerFilename{Ansi.Reset}");
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

        Console.WriteLine($"{Ansi.Dim}Uploading {localFile} to {filename} in {podName}...{Ansi.Reset}");
        var ok = UploadFileToPod(podName, filename, localFile);
        if (!ok)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to upload file to pod.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        Console.WriteLine($"{Ansi.Cyan}File copied to container.{Ansi.Reset}");
        return Task.FromResult(0);
    }
}
