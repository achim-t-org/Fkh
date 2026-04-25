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

        // 3. Populate the repo with template files
        var result = await UpdateDeploymentRepoCommand.UpdateDeploymentRepoAsync(deployFullRepo, fkhFullRepo, "Initial deployment repo from Fkh template", quiet: true);
        if (result != 0)
            return result;

        // 4. Print next steps
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

        return 0;
    }
}
