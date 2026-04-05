using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class StartNodeFunction : FunctionBase
{
    private readonly ILogger<StartNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FK8sScaleNode _scaleNode;

    public StartNodeFunction(ILogger<StartNodeFunction> logger, GitHubAuthService gitHub, FK8sScaleNode scaleNode)
    {
        _logger = logger;
        _gitHub = gitHub;
        _scaleNode = scaleNode;
    }

    [Function("StartNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "StartNode")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "StartNode", _scaleNode.StartNodeAsync);
}
