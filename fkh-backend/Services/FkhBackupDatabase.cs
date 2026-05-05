using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhBackupDatabase : FkhBackupDatabaseBase
{
    public FkhBackupDatabase(ILogger<FkhBackupDatabase> logger) : base(logger) { }

    public async Task<object> BackupDatabaseAsync(Dictionary<string, string> parameters)
    {
        var githubUsername = parameters["_githubUsername"];
        var containerName = ResolveAppName(parameters);
        var backupName = parameters["backupName"];
        var backupVersion = parameters["backupVersion"];
        var databaseName = containerName;

        return await BackupDatabaseToStorageAsync(githubUsername, databaseName, backupName, backupVersion);
    }
}
