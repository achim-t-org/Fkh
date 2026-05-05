using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class MountTenantFunction : FunctionBase
{
    private readonly ILogger<MountTenantFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhMountTenant _mountTenant;

    public MountTenantFunction(
        ILogger<MountTenantFunction> logger,
        GitHubAuthService gitHub,
        FkhMountTenant mountTenant)
    {
        _logger = logger;
        _gitHub = gitHub;
        _mountTenant = mountTenant;
    }

    [Function("MountTenant")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "MountTenant")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "MountTenant", _mountTenant.MountTenantAsync);
}
