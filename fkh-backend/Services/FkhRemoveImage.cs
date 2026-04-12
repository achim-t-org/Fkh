using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhRemoveImage : FkhServiceBase
{
    public FkhRemoveImage(ILogger<FkhRemoveImage> logger) : base(logger) { }

    public async Task<object> RemoveImageAsync(Dictionary<string, string> parameters)
    {
        var repository = parameters["repository"];
        var tag = parameters.TryGetValue("tag", out var t) ? t : null;

#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var client = new ContainerRegistryClient(new Uri($"https://{AcrLoginServer}"), credential);
        var results = new List<string>();

        if (!string.IsNullOrEmpty(tag))
        {
            // Delete a specific tag
            Logger.LogInformation("Deleting tag '{Tag}' from repository '{Repository}'...", tag, repository);
            var artifact = client.GetArtifact(repository, tag);
            await artifact.DeleteAsync();
            results.Add($"Tag '{tag}' deleted from repository '{repository}'");

            // Also delete the corresponding database backup blob if it exists
            results.Add(await TryDeleteDatabaseBackupAsync(credential, $"{repository}:{tag}"));
        }
        else
        {
            // Delete the entire repository
            Logger.LogInformation("Deleting repository '{Repository}'...", repository);
            var repo = client.GetRepository(repository);

            // Collect all tags first for database backup cleanup
            var tags = new List<string>();
            await foreach (var manifest in repo.GetAllManifestPropertiesAsync())
            {
                tags.AddRange(manifest.Tags);
            }

            await repo.DeleteAsync();
            results.Add($"Repository '{repository}' deleted");

            // Clean up database backup blobs for all tags
            foreach (var repoTag in tags)
            {
                results.Add(await TryDeleteDatabaseBackupAsync(credential, $"{repository}:{repoTag}"));
            }
        }

        Logger.LogInformation("Image removal complete for repository '{Repository}'.", repository);
        return new { Repository = repository, Tag = tag, Results = results };
    }

    private async Task<string> TryDeleteDatabaseBackupAsync(ManagedIdentityCredential credential, string imageTag)
    {
        try
        {
            // The database backup blob name matches the image tag format from GetImageTag
            var blobName = imageTag.Contains(':') ? imageTag.Split(':').Last() : imageTag;
            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{DbsStorageAccountName}.blob.core.windows.net"), credential);
            var containerClient = blobServiceClient.GetBlobContainerClient("cronus");
            var blobClient = containerClient.GetBlobClient(blobName);
            if (await blobClient.ExistsAsync())
            {
                await blobClient.DeleteAsync();
                return $"Database backup '{blobName}' deleted";
            }
            return $"Database backup '{blobName}' not found (skipped)";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete database backup for '{ImageTag}'", imageTag);
            return $"Database backup cleanup failed: {ex.Message}";
        }
    }
}
