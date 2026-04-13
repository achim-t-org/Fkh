using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class GetContainerLogFunction : FunctionBase
{
    private readonly ILogger<GetContainerLogFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhGetContainerLog _getContainerLog;

    public GetContainerLogFunction(ILogger<GetContainerLogFunction> logger, GitHubAuthService gitHub, FkhGetContainerLog getContainerLog)
    {
        _logger = logger;
        _gitHub = gitHub;
        _getContainerLog = getContainerLog;
    }

    [Function("GetContainerLog")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetContainerLog")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "GetContainerLog", _getContainerLog.GetContainerLogAsync);
}
