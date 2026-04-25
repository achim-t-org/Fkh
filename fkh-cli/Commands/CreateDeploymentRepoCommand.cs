sealed class CreateDeploymentRepoCommand : ClientCommand
{
    public override string Name => "CreateDeploymentRepo";
    public override string Description => "Creates a private GitHub repo with deployment workflow templates for your Fkh fork.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "deploymentRepo", Type = "string", Description = "Owner/name of the deployment repo to create (e.g. myorg/fkh-deploy)", Required = true },
        new() { Name = "fkhRepo",        Type = "string", Description = "Owner/name of the Fkh fork to use (default: Freddy-DK/Fkh)", Required = false },
    ];

    public override async Task<int> ExecuteAsync(string[] args, CliSettings settings, bool asJson)
    {
        var parameters = ParseClientArgs(args);

        if (!parameters.TryGetValue("deploymentRepo", out var deployFullRepo) || string.IsNullOrWhiteSpace(deployFullRepo))
        {
            Console.Error.WriteLine($"{Ansi.Red}--deploymentRepo is required (e.g. myorg/fkh-deploy).{Ansi.Reset}");
            return 1;
        }

        var fkhFullRepo = parameters.TryGetValue("fkhRepo", out var fr) && !string.IsNullOrWhiteSpace(fr) ? fr : "Freddy-DK/Fkh";

        Console.WriteLine($"Creating private deployment repo: {deployFullRepo}");
        Console.WriteLine($"Using Fkh fork: {fkhFullRepo}");
        Console.WriteLine();

        // 1. Verify gh is authenticated
        var (ghExit, _, ghErr) = RunProcess("gh", ["auth", "status"]);
        if (ghExit != 0)
        {
            Console.Error.WriteLine($"{Ansi.Red}GitHub CLI is not authenticated. Run 'gh auth login' first.{Ansi.Reset}");
            Console.Error.WriteLine(ghErr);
            return 1;
        }

        // 2. Create private repo
        Console.WriteLine($"Creating private repo {deployFullRepo}...");
        var (createExit, createOut, createErr) = RunProcess("gh", ["repo", "create", deployFullRepo, "--private", "--confirm"]);
        if (createExit != 0)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to create repo: {createErr}{Ansi.Reset}");
            return 1;
        }
        Console.WriteLine($"  Created: {createOut.Trim()}");

        // 3. Clone to temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"fkh-deploy-{Guid.NewGuid():N}");
        Console.WriteLine($"Cloning {deployFullRepo}...");
        var (cloneExit, _, cloneErr) = RunProcess("gh", ["repo", "clone", deployFullRepo, tempDir]);
        if (cloneExit != 0)
        {
            Console.Error.WriteLine($"{Ansi.Red}Failed to clone repo: {cloneErr}{Ansi.Reset}");
            return 1;
        }

        try
        {
            // 4. Enumerate and fetch all files from deployment-repo/ in the Fkh fork
            Console.WriteLine($"Fetching template files from {fkhFullRepo}...");
            var templateFiles = EnumerateGitHubDirectory(fkhFullRepo, "deployment-repo");
            if (templateFiles.Count == 0)
            {
                Console.Error.WriteLine($"{Ansi.Red}No template files found in {fkhFullRepo}/deployment-repo.{Ansi.Reset}");
                return 1;
            }

            foreach (var templatePath in templateFiles)
            {
                var content = await FetchFileFromGitHubAsync(fkhFullRepo, templatePath);
                if (content is null)
                {
                    Console.Error.WriteLine($"{Ansi.Yellow}  Warning: Could not fetch {templatePath} — skipping.{Ansi.Reset}");
                    continue;
                }

                // 5. Replace Freddy-DK/Fkh with actual fkhRepo in .yml files
                if (templatePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    content = content.Replace("Freddy-DK/Fkh", fkhFullRepo);

                // Strip the "deployment-repo/" prefix to get the target path
                var relativePath = templatePath["deployment-repo/".Length..];
                var targetPath = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);
                await File.WriteAllTextAsync(targetPath, content);
                Console.WriteLine($"  {relativePath}");
            }

            // 6. Configure git identity and commit
            Console.WriteLine("Committing and pushing...");
            var (_, ghName, _) = RunProcess("gh", ["api", "user", "--jq", ".login"]);
            var (_, ghEmail, _) = RunProcess("gh", ["api", "user", "--jq", ".email // (.login + \"@users.noreply.github.com\")"]);
            ghName = ghName?.Trim(); ghEmail = ghEmail?.Trim();
            if (!string.IsNullOrEmpty(ghName))
                RunProcess("git", ["config", "user.name", ghName], tempDir);
            if (!string.IsNullOrEmpty(ghEmail))
                RunProcess("git", ["config", "user.email", ghEmail], tempDir);

            RunProcess("git", ["add", "-A"], tempDir);
            var (commitExit, _, commitErr) = RunProcess("git", ["commit", "-m", "Initial deployment repo from Fkh template"], tempDir);
            if (commitExit != 0)
            {
                Console.Error.WriteLine($"{Ansi.Red}Failed to commit: {commitErr}{Ansi.Reset}");
                return 1;
            }
            var (pushExit, _, pushErr) = RunProcess("git", ["push"], tempDir);
            if (pushExit != 0)
            {
                Console.Error.WriteLine($"{Ansi.Red}Failed to push: {pushErr}{Ansi.Reset}");
                return 1;
            }

            // 7. Print instructions
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Cyan}Deployment repo created successfully!{Ansi.Reset}");
            Console.WriteLine($"  https://github.com/{deployFullRepo}");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine($"  1. Setup Azure Identity: https://github.com/{fkhFullRepo}/blob/main/Installation/Step2-AzureIdentity.md");
            Console.WriteLine($"  2. Setup GitHub App: https://github.com/{fkhFullRepo}/blob/main/Installation/Step3-GitHubApp.md");
            Console.WriteLine($"  3. Setup GitHub Teams: https://github.com/{fkhFullRepo}/blob/main/Installation/Step4-GitHubTeams.md");
            Console.WriteLine($"  4. Edit config/deployment.tfvars in {deployFullRepo}: https://github.com/{fkhFullRepo}/blob/main/Installation/Step5-ConfigureEnvironment.md");
            Console.WriteLine("  5. Add these GitHub Secrets in the deployment repo:");
            Console.WriteLine("     - AZURE_DEPLOY_CLIENT_ID");
            Console.WriteLine("     - SQL_SA_PASSWORD");
            Console.WriteLine("     - GH_APP_PRIVATE_KEY");
            Console.WriteLine("  6. Run the 'Deploy Full Stack' workflow from the Actions tab");
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }

        return 0;
    }

    private static Task<string?> FetchFileFromGitHubAsync(string repo, string path)
    {
        // Use gh api to fetch file content (base64 encoded)
        var (exit, stdout, _) = RunProcess("gh", ["api", $"repos/{repo}/contents/{path}", "--jq", ".content"]);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return Task.FromResult<string?>(null);

        try
        {
            // GitHub API returns base64 with newlines
            var base64 = stdout.Trim().Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    private static List<string> EnumerateGitHubDirectory(string repo, string dirPath)
    {
        var files = new List<string>();
        var (exit, stdout, _) = RunProcess("gh", ["api", $"repos/{repo}/contents/{dirPath}", "--jq", ".[] | .type + \"\\t\" + .path"]);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return files;

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;
            var (type, path) = (parts[0], parts[1]);
            if (type == "file")
                files.Add(path);
            else if (type == "dir")
                files.AddRange(EnumerateGitHubDirectory(repo, path));
        }
        return files;
    }
}
