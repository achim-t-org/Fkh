using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class ListPodsFunction : FunctionBase
{
    private readonly ILogger<ListPodsFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhListPods _listPods;

    public ListPodsFunction(ILogger<ListPodsFunction> logger, GitHubAuthService gitHub, FkhListPods listPods)
    {
        _logger = logger;
        _gitHub = gitHub;
        _listPods = listPods;
    }

    [Function("ListPods")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListPods")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListPods", _listPods.ListPodsAsync);
}
