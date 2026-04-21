using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class ListPrepulledFunction : FunctionBase
{
    private readonly ILogger<ListPrepulledFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhPrepull _prepull;

    public ListPrepulledFunction(ILogger<ListPrepulledFunction> logger, GitHubAuthService gitHub, FkhPrepull prepull)
    {
        _logger = logger;
        _gitHub = gitHub;
        _prepull = prepull;
    }

    [Function("ListPrepulled")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListPrepulled")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListPrepulled", _prepull.ListPrepulledAsync);
}
