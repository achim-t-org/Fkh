using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class PublishAppFunction : FunctionBase
{
    private readonly ILogger<PublishAppFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhPublishApp _publishApp;

    public PublishAppFunction(ILogger<PublishAppFunction> logger, GitHubAuthService gitHub, FkhPublishApp publishApp)
    {
        _logger = logger;
        _gitHub = gitHub;
        _publishApp = publishApp;
    }

    [Function("PublishApp")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "PublishApp")] HttpRequestData req)
        => ExecuteWithFileAsync(req, _logger, _gitHub, "PublishApp", _publishApp.PublishAppAsync);
}
