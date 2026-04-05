using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class StopNodeFunction : FunctionBase
{
    private readonly ILogger<StopNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FK8sScaleNode _scaleNode;

    public StopNodeFunction(ILogger<StopNodeFunction> logger, GitHubAuthService gitHub, FK8sScaleNode scaleNode)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleNode = scaleNode;
    }

    [Function("StopNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StopNode")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StopNode", _scaleNode.StopNodeAsync);
}
