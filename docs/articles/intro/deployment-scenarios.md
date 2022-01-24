---
uid: deployment-scenarios
title: Deployment Scenarios
---
# Deployment Scenarios

## Console Application

```csharp
PM> install-package Akka
PM> install-package Akka.Remote
```

```csharp
using Akka;
using Akka.Actor;
using Akka.Configuration;

namespace Foo.Bar
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("my-actor-server"))
            {
                //start two services
                var service1= system.ActorOf(ServiceOne.Prop(), "service1");
                var service2 = system.ActorOf(ServiceTwo.Prop(), "service2");
                Console.ReadKey();
            }
        }
    }
}
```

## ASP.NET Core

When deploying Akka.NET in ASP.NET Core, one major concern is how to expose `actor` in ASP.NET Core controllers. We will design an `interface` for this!
```csharp
public interface IActorBridge
{
    void Tell(object message);
	Task Ask<T>(object message);
}
```
### Host Akka.NET with `IHostedService`

With the `IActorBridge` created, next is to host Akka.NET with `IHostedService` which will also implement the `IActorBridge`:

```csharp
using Akka.Actor;
using Akka.DependencyInjection;

namespace WebApplication1
{
    public class AkkaService: IHostedService, IActorBridge
    {
        private ActorSystem _actorSystem;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private IActorRef _actorRef;

        private readonly IHostApplicationLifetime _applicationLifetime;

        public AkkaService(IServiceProvider serviceProvider, IHostApplicationLifetime appLifetime, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _applicationLifetime = appLifetime;
            _configuration = configuration;
        }
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {

            var bootstrap = BootstrapSetup.Create();


            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(_serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            _actorSystem = ActorSystem.Create("akka-system", actorSystemSetup);
			
            _actorRef = _actorSystem.ActorOf(MyActor.Prop());
			
            // add a continuation task that will guarantee shutdown of application if ActorSystem terminates
            await _actorSystem.WhenTerminated.ContinueWith(tr => {
                _applicationLifetime.StopApplication();
            });
        }

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			// strictly speaking this may not be necessary - terminating the ActorSystem would also work
			// but this call guarantees that the shutdown of the cluster is graceful regardless
			await CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
		}

		public void Tell(object message)
		{
			_actorRef.Tell(message);    
		}

		public async Task<T> Ask<T>(object message)
		{
			return await _actorRef.Ask<T>(message);   
		}
	}
}

```
Typically you use a very lightweight `ActorSystem` inside ASP.NET applications, and offload heavy-duty work to a separate Windows Service via Akka.Remote / Akka.Cluster.

### Interaction Between Controllers and Akka.NET

In the sample below, we use an Web API Controller:

```csharp
[ApiController]
[Route("[controller]")]
public class WriteApiController : ControllerBase
{
        
   private readonly ILogger<WriteApiController> _logger;
   private readonly IActorBridge _bridge;

   public WriteApiController(ILogger<WriteApiController> logger, IActorBridge bridge)
   {
      _logger = logger;
      _bridge = _bridge;
   }

   [HttpPost]
   [Route("post")]
   public async Task<IActionResult> Post([FromBody] object data)
   {
       _bridge.Tell(data);
	   //you can use Ask if you need response from the Actor
       //await _bridge.Ask<T>(data);
       return Ok();
   }
}
```

### Wire up Akka.NET and ASP.NET Core in `Startup.cs`
We need to replace the default `IHostedService` with `AkkaService` and also inject `IActorBridge` and we do that with `Startup.cs`
```csharp
public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Akka.NET", Version = "v1" });
            });

            // creates instance of IPublicHashingService that can be accessed by ASP.NET
            services.AddSingleton<IActorBridge, AkkaService>();

            // starts the IHostedService, which creates the ActorSystem and actors
            services.AddHostedService<AkkaService>(sp => (AkkaService)sp.GetRequiredService<IActorBridge>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Akka.Net v1"));
            }

            app.UseRouting();

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
```
Visit our site's blog post for [Best Practices for Integrating Akka.NET with ASP.NET Core and SignalR](https://petabridge.com/blog/akkadotnet-aspnetcore/) 

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
