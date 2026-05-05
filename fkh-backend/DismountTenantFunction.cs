using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class DismountTenantFunction : FunctionBase
{
    private readonly ILogger<DismountTenantFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhDismountTenant _dismountTenant;

    public DismountTenantFunction(
        ILogger<DismountTenantFunction> logger,
        GitHubAuthService gitHub,
        FkhDismountTenant dismountTenant)
    {
        _logger = logger;
        _gitHub = gitHub;
        _dismountTenant = dismountTenant;
    }

    [Function("DismountTenant")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "DismountTenant")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "DismountTenant", _dismountTenant.DismountTenantAsync);
}
