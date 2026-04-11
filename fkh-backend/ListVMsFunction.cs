using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class ListVMsFunction : FunctionBase
{
    private readonly ILogger<ListVMsFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhListVMs _listVMs;

    public ListVMsFunction(ILogger<ListVMsFunction> logger, GitHubAuthService gitHub, FkhListVMs listVMs)
    {
        _logger = logger;
        _gitHub = gitHub;
        _listVMs = listVMs;
    }

    [Function("ListVMs")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ListVMs")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "ListVMs", _listVMs.ListVMsAsync);
}
