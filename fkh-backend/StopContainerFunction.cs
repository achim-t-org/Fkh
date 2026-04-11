using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class StopContainerFunction : FunctionBase
{
    private readonly ILogger<StopContainerFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScaleContainer _scaleContainer;

    public StopContainerFunction(ILogger<StopContainerFunction> logger, GitHubAuthService gitHub, FkhScaleContainer scaleContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleContainer = scaleContainer;
    }

    [Function("StopContainer")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StopContainer")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StopContainer", _scaleContainer.StopContainerAsync);
}
