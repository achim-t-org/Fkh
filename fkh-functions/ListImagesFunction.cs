using FKH.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FKH;

public class ListImagesFunction : FunctionBase
{
    private readonly ILogger<ListImagesFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FKHListImages _listImages;

    public ListImagesFunction(ILogger<ListImagesFunction> logger, GitHubAuthService gitHub, FKHListImages listImages)
    {
        _logger = logger;
        _gitHub = gitHub;
        _listImages = listImages;
    }

    [Function("ListImages")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListImages")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListImages", _listImages.ListImagesAsync);
}
