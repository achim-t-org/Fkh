using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class CreateContainerFunction : FunctionBase
{
    private readonly ILogger<CreateContainerFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhCreateContainer _createContainer;

    public CreateContainerFunction(ILogger<CreateContainerFunction> logger, GitHubAuthService gitHub, FkhCreateContainer createContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _createContainer = createContainer;
    }

    [Function("CreateContainer")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "CreateContainer")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "CreateContainer", _createContainer.CreateContainerAsync);
}
