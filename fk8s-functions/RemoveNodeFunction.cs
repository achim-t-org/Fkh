using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class RemoveNodeFunction : FunctionBase
{
    private readonly ILogger<RemoveNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly AksService _aks;

    public RemoveNodeFunction(ILogger<RemoveNodeFunction> logger, GitHubAuthService gitHub, AksService aks)
    {
        _logger = logger;
        _gitHub = gitHub;
        _aks = aks;
    }

    [Function("RemoveNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "remove-node")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RemoveNode", _aks.RemoveNodeAsync);
}
