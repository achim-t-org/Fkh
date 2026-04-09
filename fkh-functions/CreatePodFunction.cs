using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

public class CreatePodFunction : FunctionBase
{
    private readonly ILogger<CreatePodFunction> _logger;
    private readonly GitHubAuthService _gitHub;
    private readonly FkhCreatePod _createPod;

    public CreatePodFunction(ILogger<CreatePodFunction> logger, GitHubAuthService gitHub, FkhCreatePod createPod)
    {
        _logger = logger;
        _gitHub = gitHub;
        _createPod = createPod;
    }

    [Function("CreatePod")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "CreatePod")] HttpRequestData req)
        => ExecuteAsync(req, _logger, _gitHub, "CreatePod", _createPod.CreatePodAsync);
}
