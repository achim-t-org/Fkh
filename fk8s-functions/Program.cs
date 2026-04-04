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

        // Register FK8sService
        services.AddSingleton<FK8sService>();
    })
    .Build();

host.Run();
