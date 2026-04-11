using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class ListContainersFunction : FunctionBase
{
    private readonly ILogger<ListContainersFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhListContainers _listContainers;

    public ListContainersFunction(ILogger<ListContainersFunction> logger, GitHubAuthService gitHub, FkhListContainers listContainers)
    {
        _logger = logger;
        _gitHub = gitHub;
        _listContainers = listContainers;
    }

    [Function("ListContainers")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListContainers")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListContainers", _listContainers.ListContainersAsync);
}
