---
uid: deployment-scenarios
title: Deployment Scenarios
---

# Console Application

The console application gives immediate feedback on how a piece of software works. That how most developers test out, quickly, new products to understand its entry and exit point!

[Console Application](../deployment/console.html)

# ASP.NET Core

By using Akka.NET in your ASP.NET Core project, you can make your `Controllers` lightweight while Akka.NET handles the heavyweights.

[ASP.NET Core](../deployment/aspnet-core.html)

## Windows Service

The quickest way to get started with deploying Akka.NET as Windows Service is by creating a `BackgroundService` - [learn more](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service).
Which would look like this:

### Program.cs

```csharp
using Backgroundservice;
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
```

### AkkaService.cs

```csharp
public sealed class AkkaService : BackgroundService
    {
        private readonly ILogger<AkkaService> _logger;
        private readonly JokeService _jokeService;
        private readonly ActorSystem _actorSystem;
        private readonly IActorRef _actorRef;   

        public AkkaService(JokeService jokeService, ILogger<AkkaService> logger, IServiceProvider serviceProvider)
        {
            _jokeService = jokeService; 
            _logger = logger;
            var bootstrap = BootstrapSetup.Create();


            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            _actorSystem = ActorSystem.Create("akka-system", actorSystemSetup);
            _actorRef = _actorSystem.ActorOf(MyActor.Prop());
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                string joke = await _jokeService.GetJokeAsync();
                _actorRef.Tell(joke);   
                _logger.LogWarning(joke);

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
```

### JokeService.cs

```csharp
public class JokeService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string JokeApiUrl =
            "https://karljoke.herokuapp.com/jokes/programming/random";

        public JokeService(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<string> GetJokeAsync()
        {
            try
            {
                // The API returns an array with a single entry.
                Joke[]? jokes = await _httpClient.GetFromJsonAsync<Joke[]>(
                    JokeApiUrl, _options);

                Joke? joke = jokes?[0];

                return joke is not null
                    ? $"{joke.Setup}{Environment.NewLine}{joke.Punchline}"
                    : "No joke here...";
            }
            catch (Exception ex)
            {
                return $"That's not funny! {ex}";
            }
        }
    }

    public record Joke(int Id, string Type, string Setup, string Punchline);
```

This is of course, one way of deploying Akka.NET HEADLESS Service. Our blog post [How to Build Headless Akka.NET Services with IHostedService](https://petabridge.com/blog/akkadotnet-ihostedservice/) is a good alternative!

## Azure PaaS Worker Role

The following sample assumes that you have created a new Azure PaaS Cloud Service that contains a single
empty Worker Role. The Cloud Service project templates are added to Visual Studio by installing the
[Azure .Net SDK](http://azure.microsoft.com/en-gb/downloads/).

The Worker Role implementation can be tested locally using the Azure Compute Emulator before deploying to the cloud. The MSDN Azure article ["Using Emulator Express to Run and Debug a Cloud Service Locally"](https://msdn.microsoft.com/en-us/library/azure/dn339018.aspx) describes this in more detail.

The Azure PaaS Worker Role implementation is very similar to the [Windows Service](#windows-service).
The quickest way to get started with Akka.Net is to create a simple Worker Role which invokes the top-level
user-actor in the RunAsync() method, as follows:

### WorkerRole.cs

```csharp
using Akka.Actor;

namespace MyActorWorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        private ActorSystem _actorSystem;

        public override bool OnStart()
        {
            // Setup the Actor System
            _actorSystem = ActorSystem.Create("MySystem");

            return (base.OnStart());
        }

        public override void OnStop()
        {
            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            // Shutdown the Actor System
            _actorSystem.Shutdown();

            base.OnStop();
        }

        public override void Run()
        {
            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // Create an instance to the top-level user Actor
            var workerRoleActor = _actorSystem.ActorOf<WorkerRoleActor>("WorkerRole");

            // Send a message to the Actor
            workerRoleActor.Tell(new WorkerRoleMessage("Hello World!"));

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }
}
```
