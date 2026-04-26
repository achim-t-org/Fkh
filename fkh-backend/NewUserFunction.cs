using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class NewUserFunction : FunctionBase
{
    private readonly ILogger<NewUserFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhNewUser _newUser;

    public NewUserFunction(ILogger<NewUserFunction> logger, GitHubAuthService gitHub, FkhNewUser newUser)
    {
        _logger = logger;
        _gitHub = gitHub;
        _newUser = newUser;
    }

    [Function("NewUser")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "NewUser")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "NewUser", _newUser.NewUserAsync);
}
