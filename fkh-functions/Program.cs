using System.Text.Json;
using FKH.Services;
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
        services.AddSingleton<FKHCreateNode>();
        services.AddSingleton<FKHRemoveNode>();
        services.AddSingleton<FKHScaleNode>();
        services.AddSingleton<FKHListNodes>();
        services.AddSingleton<FKHAutoStop>();
        services.AddSingleton<FKHAllowSqlAccess>();
        services.AddSingleton<FKHListImages>();
    })
    .Build();

host.Run();
