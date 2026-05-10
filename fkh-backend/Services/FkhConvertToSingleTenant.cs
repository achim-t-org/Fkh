using k8s;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhConvertToSingleTenant : FkhServiceBase
{
    public FkhConvertToSingleTenant(ILogger<FkhConvertToSingleTenant> logger) : base(logger) { }

    public async Task<object> ConvertToSingleTenantAsync(Dictionary<string, string> parameters)
    {
        var containerName = ResolveAppName(parameters);
        var tenant = parameters.TryGetValue("tenant", out var t) && !string.IsNullOrWhiteSpace(t) ? t : "default";
        var doNotRestart = parameters.TryGetValue("doNotRestart", out var flag)
            && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        var appDatabaseName = containerName; // app database is named after the container
        var tenantDatabaseName = $"{containerName}-{tenant}";
        var deploymentName = $"{containerName}-deployment";

        var client = await GetKubernetesClientAsync();

        // Find the BC pod for this container
        var pods = await client.ListNamespacedPodAsync(Namespace, labelSelector: $"app={containerName}");
        var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
            ?? throw new InvalidOperationException($"No running container found for '{containerName}'. Make sure the container is started and ready.");

        var podName = pod.Metadata.Name;
        var bcContainerName = pod.Spec.Containers[0].Name;

        // Step 1: Stop the service tier so we can safely modify the databases
        Logger.LogInformation("Step 1/5: Stopping service tier for '{Container}'...", containerName);
        await ExecInBcPodPwshAsync(client, podName, bcContainerName,
            ". 'C:\\run\\prompt.ps1' -silent; Stop-NAVServerInstance -ServerInstance $ServerInstance -Force");
        Logger.LogInformation("Service tier stopped.");

        var mssqlPod = await FindMssqlPodAsync(client);

        // Step 2: Copy application tables from app database into the tenant database
        // Export-NAVApplication doesn't support SQL auth, so we replicate its logic via direct SQL:
        // copy all tables that exist in the app database but not in the tenant database.
        Logger.LogInformation("Step 2/5: Copying application tables from '{AppDb}' to '{TenantDb}'...", appDatabaseName, tenantDatabaseName);
        var copySql = "DECLARE @sql NVARCHAR(MAX) = N'';" +
            $" SELECT @sql = @sql + N'SELECT * INTO [{tenantDatabaseName}].[dbo].' + QUOTENAME(t.name) + N' FROM [{appDatabaseName}].[dbo].' + QUOTENAME(t.name) + N'; '" +
            $" FROM [{appDatabaseName}].sys.tables t" +
            $" WHERE t.name NOT IN (SELECT name FROM [{tenantDatabaseName}].sys.tables);" +
            " EXEC sp_executesql @sql; PRINT 'COPY_TABLES_OK'";
        var copyScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q \"{copySql}\"";
        var copyResult = await ExecInMssqlPodAsync(client, mssqlPod, copyScript);
        if (!copyResult.Stdout.Contains("COPY_TABLES_OK"))
            throw new InvalidOperationException($"Failed to copy application tables from '{appDatabaseName}' to '{tenantDatabaseName}'. {copyResult}");
        Logger.LogInformation("Application tables copied.");

        // Step 3: Drop the old app database and rename the tenant database to the app database name
        // so the DatabaseName in custom config doesn't need to change.
        Logger.LogInformation("Step 3/5: Dropping old app database '{AppDb}'...", appDatabaseName);
        var dropSql = $"ALTER DATABASE [{appDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{appDatabaseName}]";
        var dropScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q \"{dropSql}\" && echo 'DROP_COMPLETE'";
        var dropResult = await ExecInMssqlPodAsync(client, mssqlPod, dropScript);
        if (!dropResult.Stdout.Contains("DROP_COMPLETE"))
            throw new InvalidOperationException($"Failed to drop old app database '{appDatabaseName}'. {dropResult}");

        Logger.LogInformation("Step 4/5: Renaming database '{TenantDb}' to '{AppDb}'...", tenantDatabaseName, appDatabaseName);
        var renameSql = $"ALTER DATABASE [{tenantDatabaseName}] MODIFY NAME = [{appDatabaseName}]";
        var renameScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q \"{renameSql}\" && echo 'RENAME_COMPLETE'";
        var renameResult = await ExecInMssqlPodAsync(client, mssqlPod, renameScript);
        if (!renameResult.Stdout.Contains("RENAME_COMPLETE"))
            throw new InvalidOperationException($"Failed to rename database '{tenantDatabaseName}' to '{appDatabaseName}'. {renameResult}");
        Logger.LogInformation("Database renamed from '{TenantDb}' to '{AppDb}'.", tenantDatabaseName, appDatabaseName);

        // Step 5: Remove the multitenant env var from the deployment.
        // This triggers a new pod with the correct single-tenant configuration.
        Logger.LogInformation("Step 5/5: Updating deployment to remove 'multitenant' env var...");
        var deployment = await client.ReadNamespacedDeploymentAsync(deploymentName, Namespace);
        var container = deployment.Spec.Template.Spec.Containers[0];
        container.Env = container.Env?.Where(e => e.Name != "multitenant").ToList();
        if (doNotRestart)
            deployment.Spec.Replicas = 0;
        await client.ReplaceNamespacedDeploymentAsync(deployment, deploymentName, Namespace);
        Logger.LogInformation("Deployment updated.");

        Logger.LogInformation("Container '{Container}' converted to single-tenant successfully.", containerName);

        var restartMsg = doNotRestart
            ? " Container is stopped (--doNotRestart). Start it to apply the changes."
            : " Container is restarting with single-tenant configuration.";

        return new
        {
            message = $"Container '{containerName}' converted to single-tenant.{restartMsg}",
            containerName,
            tenant,
            databaseName = appDatabaseName
        };
    }
}
