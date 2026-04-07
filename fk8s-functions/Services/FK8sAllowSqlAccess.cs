using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sAllowSqlAccess : FK8sServiceBase
{
    public const string ServicePrefix = "mssql-ext-";
    public const string PolicyPrefix = "mssql-allow-ip-";
    public const string AutoRevokeAnnotation = "fk8s/sql-access-revoke-at";

    public FK8sAllowSqlAccess(ILogger<FK8sAllowSqlAccess> logger) : base(logger) { }

    public async Task<string> AllowSqlAccessAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var ip = parameters["ip"];
        var hours = parameters.TryGetValue("hours", out var h) && double.TryParse(h, out var parsed) && parsed > 0
            ? parsed
            : 2;

        var sanitizedUser = SanitizeAppName(githubUsername);
        var serviceName = $"{ServicePrefix}{sanitizedUser}";
        var policyName = $"{PolicyPrefix}{sanitizedUser}";
        var cidr = ip.Contains('/') ? ip : $"{ip}/32";
        var revokeAt = DateTimeOffset.UtcNow.AddHours(hours);

        Logger.LogInformation(
            "Allowing SQL access for user '{User}' from {Cidr} for {Hours}h (until {RevokeAt} UTC).",
            githubUsername, cidr, hours, revokeAt);

        var client = await GetKubernetesClientAsync();

        // ── Create or update LoadBalancer service ─────────────────────────────────
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = Namespace,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "mssql",
                    ["fk8s/purpose"] = "sql-external-access",
                    ["fk8s/owner"] = sanitizedUser,
                },
                Annotations = new Dictionary<string, string>
                {
                    [AutoRevokeAnnotation] = revokeAt.UtcDateTime.ToString("o"),
                },
            },
            Spec = new V1ServiceSpec
            {
                Type = "LoadBalancer",
                ExternalTrafficPolicy = "Local",
                LoadBalancerSourceRanges = new List<string> { cidr },
                Selector = new Dictionary<string, string> { ["app"] = "mssql" },
                Ports = new List<V1ServicePort>
                {
                    new() { Protocol = "TCP", Port = 1433, TargetPort = 1433 },
                },
            },
        };

        try
        {
            await client.ReadNamespacedServiceAsync(serviceName, Namespace);
            await client.ReplaceNamespacedServiceAsync(service, serviceName, Namespace);
            Logger.LogInformation("Updated existing SQL access service '{Service}'.", serviceName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await client.CreateNamespacedServiceAsync(service, Namespace);
            Logger.LogInformation("Created SQL access service '{Service}'.", serviceName);
        }

        // ── Create or update NetworkPolicy ────────────────────────────────────────
        var policy = new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = policyName,
                NamespaceProperty = Namespace,
                Labels = new Dictionary<string, string>
                {
                    ["fk8s/purpose"] = "sql-external-access",
                    ["fk8s/owner"] = sanitizedUser,
                },
                Annotations = new Dictionary<string, string>
                {
                    [AutoRevokeAnnotation] = revokeAt.UtcDateTime.ToString("o"),
                },
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = "mssql" },
                },
                PolicyTypes = new List<string> { "Ingress" },
                Ingress = new List<V1NetworkPolicyIngressRule>
                {
                    new()
                    {
                        FromProperty = new List<V1NetworkPolicyPeer>
                        {
                            new() { IpBlock = new V1IPBlock { Cidr = cidr } },
                        },
                        Ports = new List<V1NetworkPolicyPort>
                        {
                            new() { Protocol = "TCP", Port = 1433 },
                        },
                    },
                },
            },
        };

        try
        {
            await client.ReadNamespacedNetworkPolicyAsync(policyName, Namespace);
            await client.ReplaceNamespacedNetworkPolicyAsync(policy, policyName, Namespace);
            Logger.LogInformation("Updated existing SQL access network policy '{Policy}'.", policyName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await client.CreateNamespacedNetworkPolicyAsync(policy, Namespace);
            Logger.LogInformation("Created SQL access network policy '{Policy}'.", policyName);
        }

        // ── Wait for external IP assignment ───────────────────────────────────────
        string? externalIp = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var svc = await client.ReadNamespacedServiceAsync(serviceName, Namespace);
            var ingress = svc.Status?.LoadBalancer?.Ingress?.FirstOrDefault();
            if (ingress is not null)
            {
                externalIp = ingress.Ip ?? ingress.Hostname;
                break;
            }
        }

        var endpoint = externalIp is not null ? $"{externalIp},1433" : "(pending — check service status)";

        // ── Create SQL login and database users if mySqlPassword is set ───────────
        var sqlLoginInfo = "";
        if (parameters.TryGetValue("mySqlPassword", out var mySqlPassword) && !string.IsNullOrWhiteSpace(mySqlPassword))
        {
            var sqlLogin = sanitizedUser.Replace("'", "''");
            var escapedPassword = mySqlPassword.Replace("'", "''");

            var podName = await FindMssqlPodAsync(client);

            // Create or update the server-level login
            var loginScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q " +
                $"\"IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{sqlLogin}') " +
                $"BEGIN CREATE LOGIN [{sqlLogin}] WITH PASSWORD = '{escapedPassword}', CHECK_POLICY = OFF; END " +
                $"ELSE BEGIN ALTER LOGIN [{sqlLogin}] WITH PASSWORD = '{escapedPassword}'; END; " +
                $"GRANT VIEW ANY DATABASE TO [{sqlLogin}]; " +
                $"GRANT CONNECT SQL TO [{sqlLogin}];\"";
            var loginResult = await ExecInMssqlPodAsync(client, podName, loginScript);
            Logger.LogInformation("Created/updated SQL login '{Login}'. Output: {Output}", sqlLogin, loginResult);

            // Find all databases matching <username>_*
            var listDbScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -h -1 -W " +
                $"-Q \"SELECT name FROM sys.databases WHERE name LIKE '{sqlLogin}[_]%'\"";
            var dbResult = await ExecInMssqlPodAsync(client, podName, listDbScript);
            var databases = dbResult.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l) && l != "name" && !l.StartsWith("---") && !l.StartsWith("("))
                .ToList();

            foreach (var db in databases)
            {
                var safeDb = db.Replace("'", "''");
                var userScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q " +
                    $"\"USE [{safeDb}]; " +
                    $"IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{sqlLogin}') " +
                    $"BEGIN CREATE USER [{sqlLogin}] FOR LOGIN [{sqlLogin}]; END; " +
                    $"ALTER ROLE [db_owner] ADD MEMBER [{sqlLogin}];\"";                await ExecInMssqlPodAsync(client, podName, userScript);
                Logger.LogInformation("Granted db_owner on '{Database}' to '{Login}'.", db, sqlLogin);
            }

            sqlLoginInfo = databases.Count > 0
                ? $"\n  SQL Login: {sqlLogin} (db_owner on {string.Join(", ", databases)})"
                : $"\n  SQL Login: {sqlLogin} (no databases matching '{sqlLogin}_*' found)";
        }

        return $"SQL access granted for user '{githubUsername}'.\n" +
               $"  Allowed IP: {cidr}\n" +
               $"  SQL Endpoint: {endpoint}\n" +
               $"  Auto-revoke: {revokeAt:yyyy-MM-dd HH:mm} UTC ({hours}h)" +
               sqlLoginInfo;
    }

    public async Task<string> RevokeSqlAccessAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var sanitizedUser = SanitizeAppName(githubUsername);

        return await RevokeForUserAsync(sanitizedUser, githubUsername);
    }

    public async Task<string> RevokeForUserAsync(string sanitizedUser, string displayName)
    {
        var serviceName = $"{ServicePrefix}{sanitizedUser}";
        var policyName = $"{PolicyPrefix}{sanitizedUser}";

        Logger.LogInformation("Revoking SQL access for user '{User}'.", displayName);
        var client = await GetKubernetesClientAsync();

        var removed = new List<string>();

        // ── Drop the SQL login (cascades to all database users) ───────────────────
        try
        {
            var sqlLogin = sanitizedUser.Replace("'", "''");
            var podName = await FindMssqlPodAsync(client);

            // Drop database users first (login can't be dropped while users exist)
            var listDbScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -h -1 -W " +
                $"-Q \"SELECT name FROM sys.databases WHERE name LIKE '{sqlLogin}[_]%'\"";
            var dbResult = await ExecInMssqlPodAsync(client, podName, listDbScript);
            var databases = dbResult.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l) && l != "name" && !l.StartsWith("---") && !l.StartsWith("("))
                .ToList();

            foreach (var db in databases)
            {
                var safeDb = db.Replace("'", "''");
                var dropUserScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q " +
                    $"\"USE [{safeDb}]; " +
                    $"IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{sqlLogin}') " +
                    $"DROP USER [{sqlLogin}];\"";
                await ExecInMssqlPodAsync(client, podName, dropUserScript);
            }

            var dropLoginScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q " +
                $"\"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{sqlLogin}') " +
                $"DROP LOGIN [{sqlLogin}];\"";
            await ExecInMssqlPodAsync(client, podName, dropLoginScript);
            removed.Add($"SQL login '{sqlLogin}'");
            Logger.LogInformation("Dropped SQL login '{Login}'.", sqlLogin);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to drop SQL login for '{User}' (may not exist).", displayName);
        }

        try
        {
            await client.DeleteNamespacedServiceAsync(serviceName, Namespace);
            removed.Add($"Service '{serviceName}'");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone
        }

        try
        {
            await client.DeleteNamespacedNetworkPolicyAsync(policyName, Namespace);
            removed.Add($"NetworkPolicy '{policyName}'");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone
        }

        if (removed.Count == 0)
        {
            return $"No SQL access resources found for user '{displayName}'.";
        }

        Logger.LogInformation("Revoked SQL access for user '{User}': {Resources}", displayName, string.Join(", ", removed));
        return $"SQL access revoked for user '{displayName}'.\n  Removed: {string.Join(", ", removed)}";
    }

    public async Task CheckAndRevokeExpiredAccessAsync()
    {
        Logger.LogInformation("Checking for expired SQL access grants...");
        var client = await GetKubernetesClientAsync();

        var services = await client.ListNamespacedServiceAsync(Namespace, labelSelector: "fk8s/purpose=sql-external-access");
        var revoked = 0;

        foreach (var svc in services.Items)
        {
            if (svc.Metadata.Annotations == null ||
                !svc.Metadata.Annotations.TryGetValue(AutoRevokeAnnotation, out var revokeAtStr))
                continue;

            if (!DateTimeOffset.TryParse(revokeAtStr, out var revokeAt))
            {
                Logger.LogWarning("Invalid revoke annotation '{Value}' on service '{Service}'.", revokeAtStr, svc.Metadata.Name);
                continue;
            }

            if (DateTimeOffset.UtcNow >= revokeAt)
            {
                var owner = svc.Metadata.Labels != null && svc.Metadata.Labels.TryGetValue("fk8s/owner", out var o) ? o : "unknown";
                Logger.LogInformation("Auto-revoking expired SQL access for '{Owner}'.", owner);
                await RevokeForUserAsync(owner, owner);
                revoked++;
            }
        }

        Logger.LogInformation("SQL access revoke check complete. Revoked {Count} grant(s).", revoked);
    }
}
