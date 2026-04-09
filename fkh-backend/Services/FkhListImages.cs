using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using k8s;
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

        // Check which images are currently in use by Kubernetes deployments
        var lastUsedMap = await GetLastUsedTimesAsync();

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

                    // Look up last used time for this repo:tag
                    var pullKey = $"{repoName}:{tag}";
                    var lastUsed = lastUsedMap.TryGetValue(pullKey, out var usedTime)
                        ? usedTime.ToString("yyyy-MM-dd HH:mm")
                        : "never";

                    sb.Append($"\n    {tag}  ({size}, {updated}, last used: {lastUsed})");
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

    /// <summary>
    /// Checks Kubernetes deployments to find the most recent creation time for each image tag.
    /// </summary>
    private async Task<Dictionary<string, DateTimeOffset>> GetLastUsedTimesAsync()
    {
        var result = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var k8sClient = await GetKubernetesClientAsync();
            var deployments = await k8sClient.ListNamespacedDeploymentAsync(Namespace);

            foreach (var deployment in deployments.Items)
            {
                var containers = deployment.Spec?.Template?.Spec?.Containers;
                if (containers == null) continue;

                var createdAt = deployment.Metadata?.CreationTimestamp;
                if (!createdAt.HasValue) continue;

                foreach (var container in containers)
                {
                    var image = container.Image;
                    if (string.IsNullOrEmpty(image) || !image.Contains(AcrLoginServer))
                        continue;

                    // Extract repo:tag from full image reference (e.g. myacr.azurecr.io/businesscentral:v1)
                    var imageWithoutRegistry = image[(image.IndexOf('/') + 1)..];
                    if (!result.TryGetValue(imageWithoutRegistry, out var existing) || createdAt.Value > existing)
                    {
                        result[imageWithoutRegistry] = createdAt.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to query Kubernetes for image usage times.");
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
