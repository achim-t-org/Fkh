using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class AddPrepullFunction : FunctionBase
{
    private readonly ILogger<AddPrepullFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhPrepull _prepull;

    public AddPrepullFunction(ILogger<AddPrepullFunction> logger, GitHubAuthService gitHub, FkhPrepull prepull)
    {
        _logger = logger;
        _gitHub = gitHub;
        _prepull = prepull;
    }

    [Function("AddPrepull")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "AddPrepull")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "AddPrepull", _prepull.AddPrepullAsync);
}
