using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class ListNodesFunction : FunctionBase
{
    private readonly ILogger<ListNodesFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FK8sListNodes _listNodes;

    public ListNodesFunction(ILogger<ListNodesFunction> logger, GitHubAuthService gitHub, FK8sListNodes listNodes)
    {
        _logger = logger;
        _gitHub = gitHub;
        _listNodes = listNodes;
    }

    [Function("ListNodes")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListNodes")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListNodes", _listNodes.ListNodesAsync);
}
