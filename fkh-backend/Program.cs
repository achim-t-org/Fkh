using System.Text.Json;
using Fkh.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton(jsonOptions);
        // Register GitHubAuthService with a named HttpClient
        services.AddHttpClient<GitHubAuthService>();

        // Register GitHub App token service for triggering workflows
        services.AddHttpClient<GitHubAppTokenService>();

        // Register AKS operation services
        services.AddSingleton<FkhCreatePod>();
        services.AddSingleton<FkhRemovePod>();
        services.AddSingleton<FkhScalePod>();
        services.AddSingleton<FkhListPods>();
        services.AddSingleton<FkhAutoStop>();
        services.AddSingleton<FkhAllowSqlAccess>();
        services.AddSingleton<FkhListImages>();
        services.AddSingleton<FkhListNodes>();
    })
    .Build();

host.Run();
