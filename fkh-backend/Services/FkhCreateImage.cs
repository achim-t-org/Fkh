using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Fkh.Models;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhCreateImage : FkhServiceBase
{
    private readonly GitHubAppTokenService _gitHubAppTokenService;

    public FkhCreateImage(ILogger<FkhCreateImage> logger, GitHubAppTokenService gitHubAppTokenService) : base(logger)
    {
        _gitHubAppTokenService = gitHubAppTokenService;
    }

    public async Task<object> CreateImageAsync(Dictionary<string, string> parameters)
    {
        var artifactUrl = parameters["artifactUrl"];

        var imageTag = GetImageTag(artifactUrl);
        var fullImage = $"{AcrLoginServer}/{AcrRepository}:{imageTag}";

        Logger.LogInformation("Checking ACR for image {Image}", fullImage);

#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var client = new ContainerRegistryClient(new Uri($"https://{AcrLoginServer}"), credential);

        try
        {
            var artifact = client.GetArtifact(AcrRepository, imageTag);
            await artifact.GetManifestPropertiesAsync();
            return new { Image = fullImage, Message = "Image already exists." };
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            Logger.LogInformation("Image not found in ACR. Triggering createImages workflow for {ArtifactUrl}...", artifactUrl);
            await _gitHubAppTokenService.TriggerCreateImagesWorkflowAsync(artifactUrl);
            throw new RetryAfterException(
                $"Image does not exist yet: {fullImage}. The createImages workflow has been triggered. Waiting for completion...",
                retryAfterSeconds: 300);
        }
    }
}
