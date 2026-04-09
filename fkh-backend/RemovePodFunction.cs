using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class RemovePodFunction : FunctionBase
{
    private readonly ILogger<RemovePodFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhRemovePod _removePod;

    public RemovePodFunction(ILogger<RemovePodFunction> logger, GitHubAuthService gitHub, FkhRemovePod removePod)
    {
        _logger = logger;
        _gitHub = gitHub;
        _removePod = removePod;
    }

    [Function("RemovePod")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RemovePod")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "RemovePod", _removePod.RemovePodAsync);
}
