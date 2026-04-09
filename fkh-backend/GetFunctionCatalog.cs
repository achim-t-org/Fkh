using System.Text.Json;
using Fkh.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace Fkh;

public class GetFunctionCatalog
{
    private readonly JsonSerializerOptions _jsonOptions;

    public GetFunctionCatalog(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    [Function("GetFunctionCatalog")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "functions")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var catalog = new FunctionCatalogResponse
        {
            Functions = FunctionCatalog.Functions
        };
        await response.WriteStringAsync(JsonSerializer.Serialize(catalog, _jsonOptions));

        return response;
    }
}
