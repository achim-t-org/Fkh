using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class RemovePrepullFunction : FunctionBase
{
    private readonly ILogger<RemovePrepullFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhPrepull _prepull;

    public RemovePrepullFunction(ILogger<RemovePrepullFunction> logger, GitHubAuthService gitHub, FkhPrepull prepull)
    {
        _logger = logger;
        _gitHub = gitHub;
        _prepull = prepull;
    }

    [Function("RemovePrepull")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RemovePrepull")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RemovePrepull", _prepull.RemovePrepullAsync);
}
