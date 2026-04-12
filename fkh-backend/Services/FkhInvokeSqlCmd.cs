using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhInvokeSqlCmd : FkhServiceBase
{
    public FkhInvokeSqlCmd(ILogger<FkhInvokeSqlCmd> logger) : base(logger) { }

    public async Task<object> InvokeSqlCmdAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var containerName = parameters["name"];
        var sql = parameters["sqlStmt"];

        var sanitizedUser = SanitizeAppName(githubUsername);
        var sanitizedName = SanitizeAppName(containerName);
        var databaseName = $"{sanitizedUser}-{sanitizedName}";

        Logger.LogInformation(
            "User '{User}' invoking SQL on database '{Database}'.",
            githubUsername, databaseName);

        var client = await GetKubernetesClientAsync();
        var podName = await FindMssqlPodAsync(client);

        // Verify the database exists and belongs to the user
        var checkScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -h -1 -W " +
            $"-Q \"SELECT name FROM sys.databases WHERE name = '{databaseName.Replace("'", "''")}'\"";
        var checkResult = await ExecInMssqlPodAsync(client, podName, checkScript);
        var dbExists = checkResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(l => string.Equals(l, databaseName, StringComparison.OrdinalIgnoreCase));

        if (!dbExists)
        {
            return new { Database = databaseName, Message = "Database not found. Make sure the container name is correct." };
        }

        // Execute the SQL statement against the user's database
        var safeSql = sql.Replace("\"", "\\\"").Replace("$", "\\$");
        var execScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -d \"{databaseName}\" " +
            $"-Q \"{safeSql}\"";
        var result = await ExecInMssqlPodAsync(client, podName, execScript);

        return new
        {
            Database = databaseName,
            Output = result.Stdout.TrimEnd(),
            Stderr = string.IsNullOrWhiteSpace(result.Stderr) ? null : result.Stderr.TrimEnd(),
        };
    }
}
