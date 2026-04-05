using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

public class CreateNodeFunction : FunctionBase
{
    private readonly ILogger<CreateNodeFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FK8sCreateNode _createNode;

    public CreateNodeFunction(ILogger<CreateNodeFunction> logger, GitHubAuthService gitHub, FK8sCreateNode createNode)
    {
        _logger = logger;
        _gitHub = gitHub;
        _createNode = createNode;
    }

    [Function("CreateNode")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "CreateNode")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "CreateNode", _createNode.CreateNodeAsync);
}
