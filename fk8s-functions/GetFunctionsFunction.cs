using FK8s.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace FK8s;

public class GetFunctionCatalog
{
    [Function("GetFunctionCatalog")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "functions")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new FunctionCatalogResponse
        {
            Functions = FunctionCatalog.Functions
        });

        return response;
    }
}
