using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class GetContainerEventLogFunction : FunctionBase
{
    private readonly ILogger<GetContainerEventLogFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhGetContainerEventLog _getContainerEventLog;

    public GetContainerEventLogFunction(ILogger<GetContainerEventLogFunction> logger, GitHubAuthService gitHub, FkhGetContainerEventLog getContainerEventLog)
    {
        _logger = logger;
        _gitHub = gitHub;
        _getContainerEventLog = getContainerEventLog;
    }

    [Function("GetContainerEventLog")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetContainerEventLog")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "GetContainerEventLog", _getContainerEventLog.GetContainerEventLogAsync);
}
