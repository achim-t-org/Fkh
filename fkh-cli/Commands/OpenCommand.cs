using System.Diagnostics;

sealed class OpenCommand : ClientCommand
{
    public override string Name => "Open";
    public override string Description => "Opens an interactive pwsh prompt inside a Kubernetes pod.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "name", Type = "string", Description = "Name of the pod to connect to.", Required = true },
        new() { Name = "wait", Type = "flag", Description = "Block the process until the session ends (default opens a new window).", Required = false }
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

        // Resolve container name to app label (same as server-side ResolveAppName)
        // If name contains '-', treat as full name (admin); otherwise prefix with GitHub username
        var appName = containerName.Contains('-')
            ? SanitizeAppName(containerName)
            : SanitizeAppName($"{GetGitHubUsername()}-{containerName}");

        Console.WriteLine($"{Ansi.Dim}Looking up pod for container '{containerName}' (app={appName})...{Ansi.Reset}");

        // Find the pod name via label selector
        var getPodPsi = new ProcessStartInfo
        {
            FileName = "kubectl",
            ArgumentList = { "get", "pods", "-n", "app", "-l", $"app={appName}", "-o", "jsonpath={.items[0].metadata.name}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var getPodProcess = Process.Start(getPodPsi);
        if (getPodProcess is null)
        {
            Console.Error.WriteLine($"{Ansi.Red}Could not start kubectl.{Ansi.Reset}");
            return Task.FromResult(1);
        }

        var podName = getPodProcess.StandardOutput.ReadToEnd().Trim();
        var podErr = getPodProcess.StandardError.ReadToEnd().Trim();
        getPodProcess.WaitForExit();

        if (string.IsNullOrWhiteSpace(podName))
        {
            Console.Error.WriteLine($"{Ansi.Red}No pod found for container '{containerName}' (app={appName}).{Ansi.Reset}");
            if (!string.IsNullOrWhiteSpace(podErr))
                Console.Error.WriteLine($"{Ansi.Red}{podErr}{Ansi.Reset}");
            return Task.FromResult(1);
        }

        Console.WriteLine($"{Ansi.Cyan}Opening pwsh prompt in pod {podName}...{Ansi.Reset}");

        var wait = args.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));

        // kubectl on Windows doesn't propagate terminal size to the remote pty,
        // so the remote pwsh defaults to 80x24. Work around by using PowerShell's
        // $Host.UI.RawUI to resize the terminal on startup (works in Windows containers).
        int cols, rows;
        try { cols = Console.WindowWidth; rows = Console.WindowHeight; }
        catch { cols = 120; rows = 50; }

        var sizeCmd = $"try {{ $r = $Host.UI.RawUI; $r.BufferSize = [System.Management.Automation.Host.Size]::new({cols}, 9999); $r.WindowSize = [System.Management.Automation.Host.Size]::new({cols}, {rows}) }} catch {{}}";

        if (wait)
        {
            var psiInline = new ProcessStartInfo
            {
                FileName = "kubectl",
                ArgumentList = { "exec", "-it", podName, "-n", "app", "--", "pwsh", "-NoExit", "-c", sizeCmd },
                UseShellExecute = false,
            };

            using var process = Process.Start(psiInline);
            if (process is null)
            {
                Console.Error.WriteLine($"{Ansi.Red}Could not start kubectl.{Ansi.Reset}");
                return Task.FromResult(1);
            }

            process.WaitForExit();
            return Task.FromResult(process.ExitCode);
        }

        // Default: launch in a new terminal window so the current process returns immediately.
        var kubectlArgs = new[] { "exec", "-it", podName, "-n", "app", "--", "pwsh", "-NoExit", "-c", sizeCmd };
        if (!LaunchInNewTerminal("kubectl", kubectlArgs))
        {
            Console.Error.WriteLine($"{Ansi.Yellow}Could not open a new terminal window — running inline (use --wait to suppress this warning).{Ansi.Reset}");
            var psiFallback = new ProcessStartInfo
            {
                FileName = "kubectl",
                UseShellExecute = false,
            };
            foreach (var a in kubectlArgs)
                psiFallback.ArgumentList.Add(a);
            using var fallback = Process.Start(psiFallback);
            if (fallback is null)
            {
                Console.Error.WriteLine($"{Ansi.Red}Could not start kubectl.{Ansi.Reset}");
                return Task.FromResult(1);
            }
            fallback.WaitForExit();
            return Task.FromResult(fallback.ExitCode);
        }
        return Task.FromResult(0);
    }
}
