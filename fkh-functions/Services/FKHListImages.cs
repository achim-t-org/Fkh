using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Fkh.Services;

public class FkhListImages : FkhServiceBase
{
    public FkhListImages(ILogger<FkhListImages> logger) : base(logger) { }

    public async Task<string> ListImagesAsync(Dictionary<string, string> parameters)
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var client = new ContainerRegistryClient(new Uri($"https://{AcrLoginServer}"), credential);

        // Query Log Analytics for last pull timestamps (best-effort)
        var lastPullMap = await GetLastPullTimesAsync(credential);

        var sb = new StringBuilder();
        var count = 0;

        await foreach (var repoName in client.GetRepositoryNamesAsync())
        {
            var repository = client.GetRepository(repoName);
            var properties = await repository.GetPropertiesAsync();

            sb.Append($"\n\n  Repository: {repoName}");
            sb.Append($"\n    Tags: {properties.Value.RegistryLoginServer}");

            await foreach (var manifest in repository.GetAllManifestPropertiesAsync(
                ArtifactManifestOrder.LastUpdatedOnDescending))
            {
                foreach (var tag in manifest.Tags)
                {
                    var size = manifest.SizeInBytes.HasValue
                        ? FormatSize(manifest.SizeInBytes.Value)
                        : "unknown";
                    var updated = manifest.LastUpdatedOn.ToString("yyyy-MM-dd HH:mm");

                    // Look up last pull time for this repo:tag
                    var pullKey = $"{repoName}:{tag}";
                    var lastPull = lastPullMap.TryGetValue(pullKey, out var pullTime)
                        ? pullTime.ToString("yyyy-MM-dd HH:mm")
                        : "never";

                    sb.Append($"\n    {tag}  ({size}, {updated}, pulled: {lastPull})");
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return "No images found in the container registry.";
        }

        return $"Images ({count}):{sb}";
    }

    private async Task<Dictionary<string, DateTimeOffset>> GetLastPullTimesAsync(ManagedIdentityCredential credential)
    {
        var result = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(LogAnalyticsWorkspaceId))
        {
            Logger.LogWarning("LOG_ANALYTICS_WORKSPACE_ID not configured — skipping last-pull lookup.");
            return result;
        }

        try
        {
            var logsClient = new LogsQueryClient(credential);

            // Query the most recent pull event per image:tag in the last 90 days
            var query = @"
                ContainerRegistryRepositoryEvents
                | where OperationName == 'Pull'
                | where TimeGenerated > ago(90d)
                | extend ImageTag = strcat(Repository, ':', Tag)
                | summarize LastPull = max(TimeGenerated) by ImageTag
            ";

            var response = await logsClient.QueryResourceAsync(
                new Azure.Core.ResourceIdentifier(LogAnalyticsWorkspaceId),
                query,
                new QueryTimeRange(TimeSpan.FromDays(90)));

            foreach (var row in response.Value.Table.Rows)
            {
                var imageTag = row.GetString("ImageTag");
                var lastPull = row.GetDateTimeOffset("LastPull");
                if (imageTag != null && lastPull.HasValue)
                {
                    result[imageTag] = lastPull.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to query Log Analytics for image pull times.");
        }

        return result;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
