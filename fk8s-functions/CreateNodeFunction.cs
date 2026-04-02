using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class CreateNodeFunction : FunctionBase
{
    private readonly ILogger<CreateNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly AksService _aks;

    public CreateNodeFunction(ILogger<CreateNodeFunction> logger, GitHubAuthService gitHub, AksService aks)
    {
        _logger = logger;
        _gitHub = gitHub;
        _aks = aks;
    }

    [Function("CreateNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "create-node")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "CreateNode", _aks.CreateNodeAsync);
}
