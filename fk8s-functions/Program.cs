using FK8s.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Register GitHubAuthService with a named HttpClient
        services.AddHttpClient<GitHubAuthService>();

        // Register GitHub App token service for triggering workflows
        services.AddHttpClient<GitHubAppTokenService>();

        // Register AKS operation services
        services.AddSingleton<FK8sCreateNode>();
        services.AddSingleton<FK8sRemoveNode>();
    })
    .Build();

host.Run();
