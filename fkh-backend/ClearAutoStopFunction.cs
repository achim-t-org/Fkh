using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class ClearAutoStopFunction : FunctionBase
{
    private readonly ILogger<ClearAutoStopFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScaleContainer _scaleContainer;

    public ClearAutoStopFunction(ILogger<ClearAutoStopFunction> logger, GitHubAuthService gitHub, FkhScaleContainer scaleContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleContainer = scaleContainer;
    }

    [Function("ClearAutoStop")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ClearAutoStop")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ClearAutoStop", _scaleContainer.ClearAutoStopForContainerAsync);
}
