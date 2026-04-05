using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class RemoveNodeFunction : FunctionBase
{
    private readonly ILogger<RemoveNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FK8sRemoveNode _removeNode;

    public RemoveNodeFunction(ILogger<RemoveNodeFunction> logger, GitHubAuthService gitHub, FK8sRemoveNode removeNode)
    {
        _logger = logger;
        _gitHub = gitHub;
        _removeNode = removeNode;
    }

    [Function("RemoveNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RemoveNode")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RemoveNode", _removeNode.RemoveNodeAsync);
}
