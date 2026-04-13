using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class StatusFunction : FunctionBase
{
    private readonly ILogger<StatusFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhStatus _status;

    public StatusFunction(ILogger<StatusFunction> logger, GitHubAuthService gitHub, FkhStatus status)
    {
        _logger = logger;
        _gitHub = gitHub;
        _status = status;
    }

    [Function("Status")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Status")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "Status", _status.GetStatusAsync);
}
