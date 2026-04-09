using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class StopPodFunction : FunctionBase
{
    private readonly ILogger<StopPodFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScalePod _scalePod;

    public StopPodFunction(ILogger<StopPodFunction> logger, GitHubAuthService gitHub, FkhScalePod scalePod)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scalePod = scalePod;
    }

    [Function("StopPod")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StopPod")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StopPod", _scalePod.StopPodAsync);
}
