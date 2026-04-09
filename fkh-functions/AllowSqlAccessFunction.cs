using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class AllowSqlAccessFunction : FunctionBase
{
    private readonly ILogger<AllowSqlAccessFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhAllowSqlAccess _sqlAccess;

    public AllowSqlAccessFunction(ILogger<AllowSqlAccessFunction> logger, GitHubAuthService gitHub, FkhAllowSqlAccess sqlAccess)
    {
        _logger = logger;
        _gitHub = gitHub;
        _sqlAccess = sqlAccess;
    }

    [Function("AllowSqlAccess")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "AllowSqlAccess")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "AllowSqlAccess", _sqlAccess.AllowSqlAccessAsync);
}
