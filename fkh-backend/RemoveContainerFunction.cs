using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class RemoveContainerFunction : FunctionBase
{
    private readonly ILogger<RemoveContainerFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhRemoveContainer _removeContainer;

    public RemoveContainerFunction(ILogger<RemoveContainerFunction> logger, GitHubAuthService gitHub, FkhRemoveContainer removeContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _removeContainer = removeContainer;
    }

    [Function("RemoveContainer")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RemoveContainer")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RemoveContainer", _removeContainer.RemoveContainerAsync);
}
