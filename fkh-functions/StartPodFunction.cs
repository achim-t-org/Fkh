using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class StartPodFunction : FunctionBase
{
    private readonly ILogger<StartPodFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScalePod _scalePod;

    public StartPodFunction(ILogger<StartPodFunction> logger, GitHubAuthService gitHub, FkhScalePod scalePod)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scalePod = scalePod;
    }

    [Function("StartPod")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StartPod")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StartPod", _scalePod.StartPodAsync);
}
