sealed class CreateDeploymentRepoCommand : ClientCommand
{
    public override string Name => "CreateDeploymentRepo";
    public override string Description => "Creates a private GitHub repo with deployment workflow templates for your Fkh fork.";
    public override List<ClientCommandParameter> Parameters =>
    [
        new() { Name = "owner",   Type = "string", Description = "GitHub org or user that owns the Fkh fork and will own the deployment repo", Required = true },
        new() { Name = "fkhRepo", Type = "string", Description = "Name of the Fkh fork (default: Fkh)", Required = false },
        new() { Name = "name",    Type = "string", Description = "Name for the new private deployment repo (default: fkh-deploy)", Required = false },
    ];

    public override async Task<int> ExecuteAsync(string[] args, CliSettings settings, bool asJson)
    {
        var parameters = ParseClientArgs(args);

        if (!parameters.TryGetValue("owner", out var owner) || string.IsNullOrWhiteSpace(owner))
        {
            Console.Error.WriteLine($"{Ansi.Red}--owner is required.{Ansi.Reset}");
            return 1;
        }

        var fkhRepo = parameters.TryGetValue("fkhRepo", out var fr) && !string.IsNullOrWhiteSpace(fr) ? fr : "Fkh";
        var repoName = parameters.TryGetValue("name", out var rn) && !string.IsNullOrWhiteSpace(rn) ? rn : "fkh-deploy";
        var fkhFullRepo = $"{owner}/{fkhRepo}";
        var deployFullRepo = $"{owner}/{repoName}";

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
            // 4. Fetch template files from the Fkh fork
            var templateFiles = new[]
            {
                "deployment-repo/.github/workflows/DeployFullStack.yml",
                "deployment-repo/.github/workflows/UpdateBackend.yml",
                "deployment-repo/.github/workflows/CreateImages.yml",
                "deployment-repo/.github/workflows/CheckForUpdates.yml",
                "deployment-repo/config/deployment.tfvars",
                "deployment-repo/README.md",
            };

            Console.WriteLine($"Fetching template files from {fkhFullRepo}...");
            foreach (var templatePath in templateFiles)
            {
                var content = await FetchFileFromGitHubAsync(fkhFullRepo, templatePath);
                if (content is null)
                {
                    Console.Error.WriteLine($"{Ansi.Yellow}  Warning: Could not fetch {templatePath} — skipping.{Ansi.Reset}");
                    continue;
                }

                // 5. Replace placeholder
                content = content.Replace("{{FKH_REPO}}", fkhFullRepo);

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
            Console.WriteLine($"  1. Edit config/deployment.tfvars in {deployFullRepo} with your settings");
            Console.WriteLine("  2. Add these GitHub Secrets in the deployment repo:");
            Console.WriteLine("     - AZURE_DEPLOY_CLIENT_ID");
            Console.WriteLine("     - SQL_SA_PASSWORD");
            Console.WriteLine("     - GH_APP_PRIVATE_KEY");
            Console.WriteLine("  3. Run the 'Deploy Full Stack' workflow from the Actions tab");
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }

        return 0;
    }

    private static async Task<string?> FetchFileFromGitHubAsync(string repo, string path)
    {
        // Use gh api to fetch file content (base64 encoded)
        var (exit, stdout, _) = RunProcess("gh", ["api", $"repos/{repo}/contents/{path}", "--jq", ".content"]);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        try
        {
            // GitHub API returns base64 with newlines
            var base64 = stdout.Trim().Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
