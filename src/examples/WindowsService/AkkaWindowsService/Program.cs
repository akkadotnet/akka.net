#region akka-windows-service-program
using AkkaWindowsService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = ".NET Joke Service";
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<AkkaService>();
        services.AddHttpClient<JokeService>();
    })
    .Build();

await host.RunAsync();
#endregion