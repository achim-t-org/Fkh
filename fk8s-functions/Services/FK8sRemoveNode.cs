using k8s;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sRemoveNode : FK8sServiceBase
{
    public FK8sRemoveNode(ILogger<FK8sRemoveNode> logger) : base(logger) { }

    public async Task<string> RemoveNodeAsync(Dictionary<string, string> parameters)
    {
        var name = parameters["name"];
        var githubUsername = parameters["_githubUsername"];
        var appName = SanitizeAppName($"{githubUsername}-{name}");
        var databaseName = appName;

        var deploymentName = $"{appName}-deployment";
        var serviceName = $"{appName}-service";
        var secretName = $"{appName}-secret";

        Logger.LogInformation("Removing node '{AppName}'...", appName);
        var client = await GetKubernetesClientAsync();
        var results = new List<string>();

        // Delete Kubernetes resources (ignore NotFound)
        results.Add(await TryDeleteAsync("Deployment", () => client.DeleteNamespacedDeploymentAsync(deploymentName, Namespace)));
        results.Add(await TryDeleteAsync("Service", () => client.DeleteNamespacedServiceAsync(serviceName, Namespace)));
        results.Add(await TryDeleteAsync("Secret", () => client.DeleteNamespacedSecretAsync(secretName, Namespace)));

        // Drop database via k8s exec (ignore if it doesn't exist)
        results.Add(await TryDropDatabaseAsync(client, databaseName));

        Logger.LogInformation("Node '{AppName}' removal complete.", appName);
        return $"Remove node '{appName}':\n  {string.Join("\n  ", results)}";
    }

    private async Task<string> TryDeleteAsync(string resourceType, Func<Task> deleteAction)
    {
        try
        {
            await deleteAction();
            return $"{resourceType} deleted";
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return $"{resourceType} not found (skipped)";
        }
    }

    private async Task<string> TryDropDatabaseAsync(Kubernetes client, string databaseName)
    {
        try
        {
            var podName = await FindMssqlPodAsync(client);
            var script = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q " +
                $"\"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; PRINT N'DATABASE_DROPPED'; END ELSE PRINT N'DATABASE_NOT_FOUND'\"";
            var result = await ExecInMssqlPodAsync(client, podName, script);
            if (result.Stdout.Contains("DATABASE_DROPPED"))
                return "Database dropped";
            if (result.Stdout.Contains("DATABASE_NOT_FOUND"))
                return "Database not found (skipped)";
            return $"Database drop uncertain: {result}";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to drop database '{DatabaseName}'", databaseName);
            return $"Database drop failed: {ex.Message}";
        }
    }
}
