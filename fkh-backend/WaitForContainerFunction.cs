using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class WaitForContainerFunction : FunctionBase
{
    private readonly ILogger<WaitForContainerFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhWaitForContainer _waitForContainer;

    public WaitForContainerFunction(ILogger<WaitForContainerFunction> logger, GitHubAuthService gitHub, FkhWaitForContainer waitForContainer)
    {
        _logger = logger;
        _gitHub = gitHub;
        _waitForContainer = waitForContainer;
    }

    [Function("WaitForContainer")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "WaitForContainer")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "WaitForContainer", _waitForContainer.WaitForContainerAsync);
}
