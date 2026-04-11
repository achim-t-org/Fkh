using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class StartContainerFunction : FunctionBase
{
    private readonly ILogger<StartContainerFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScaleContainer _scaleContainer;

    public StartContainerFunction(ILogger<StartContainerFunction> logger, GitHubAuthService gitHub, FkhScaleContainer scaleContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleContainer = scaleContainer;
    }

    [Function("StartContainer")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StartContainer")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StartContainer", _scaleContainer.StartContainerAsync);
}
