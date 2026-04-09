using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class RevokeSqlAccessFunction : FunctionBase
{
    private readonly ILogger<RevokeSqlAccessFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhAllowSqlAccess _sqlAccess;

    public RevokeSqlAccessFunction(ILogger<RevokeSqlAccessFunction> logger, GitHubAuthService gitHub, FkhAllowSqlAccess sqlAccess)
    {
        _logger = logger;
        _gitHub = gitHub;
        _sqlAccess = sqlAccess;
    }

    [Function("RevokeSqlAccess")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RevokeSqlAccess")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RevokeSqlAccess", _sqlAccess.RevokeSqlAccessAsync);
}
