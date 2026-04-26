using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class GetAppInfoFunction : FunctionBase
{
    private readonly ILogger<GetAppInfoFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhGetAppInfo _getAppInfo;

    public GetAppInfoFunction(ILogger<GetAppInfoFunction> logger, GitHubAuthService gitHub, FkhGetAppInfo getAppInfo)
    {
        _logger = logger;
        _gitHub = gitHub;
        _getAppInfo = getAppInfo;
    }

    [Function("GetAppInfo")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetAppInfo")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "GetAppInfo", _getAppInfo.GetAppInfoAsync);
}
