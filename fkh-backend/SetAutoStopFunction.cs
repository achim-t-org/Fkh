using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class SetAutoStopFunction : FunctionBase
{
    private readonly ILogger<SetAutoStopFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhScaleContainer _scaleContainer;

    public SetAutoStopFunction(ILogger<SetAutoStopFunction> logger, GitHubAuthService gitHub, FkhScaleContainer scaleContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleContainer = scaleContainer;
    }

    [Function("SetAutoStop")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "SetAutoStop")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "SetAutoStop", _scaleContainer.SetAutoStopAsync);
}
