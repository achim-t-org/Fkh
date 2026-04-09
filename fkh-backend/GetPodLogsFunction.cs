using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class GetPodLogsFunction : FunctionBase
{
    private readonly ILogger<GetPodLogsFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhGetPodLogs _getPodLogs;

    public GetPodLogsFunction(ILogger<GetPodLogsFunction> logger, GitHubAuthService gitHub, FkhGetPodLogs getPodLogs)
    {
        _logger = logger;
        _gitHub = gitHub;
        _getPodLogs = getPodLogs;
    }

    [Function("GetPodLogs")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetPodLogs")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "GetPodLogs", _getPodLogs.GetPodLogsAsync);
}
