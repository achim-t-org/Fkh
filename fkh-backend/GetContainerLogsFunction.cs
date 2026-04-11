using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class GetContainerLogsFunction : FunctionBase
{
    private readonly ILogger<GetContainerLogsFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhGetContainerLogs _getContainerLogs;

    public GetContainerLogsFunction(ILogger<GetContainerLogsFunction> logger, GitHubAuthService gitHub, FkhGetContainerLogs getContainerLogs)
    {
        _logger = logger;
        _gitHub = gitHub;
        _getContainerLogs = getContainerLogs;
    }

    [Function("GetContainerLogs")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetContainerLogs")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "GetContainerLogs", _getContainerLogs.GetContainerLogsAsync);
}
