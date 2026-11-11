<a id="akkahosting"></a>
# Akka.Hosting

HOCON-less configuration, application lifecycle management, `ActorSystem` startup, and actor instantiation for [Akka.NET](https://getakka.net/).

See the ["Introduction to Akka.Hosting - HOCON-less, "Pit of Success" Akka.NET Runtime and Configuration" video](https://www.youtube.com/watch?v=Mnb9W9ClnB0) for a walkthrough of the library and how it can save you a tremendous amount of time and trouble.

# Table Of Content

- [Supported Packages](#supported-packages)
    * [Akka.NET Core Packages](#akkanet-core-packages)
    * [Akka Persistence Plugins](#akka-persistence-plugins)
    * [Akka.Management Plugins](#akkamanagement-plugins)
        + [Akka.Management Core Package](#akkamanagement-core-package)
        + [Akka.Discovery Plugins](#akkadiscovery-plugins)
        + [Akka.Coordination Plugins](#akkacoordination-plugins)
- [Summary](#summary)
- [Dependency Injection Outside and Inside Akka.NET](#dependency-injection-outside-and-inside-akkanet)
    * [Registering Actors with the `ActorRegistry`](#registering-actors-with-the-actorregistry)
    * [Injecting Actors with `IRequiredActor<TKey>`](#injecting-actors-with-irequiredactortkey)
    * [Resolving `IRequiredActor<TKey>` within Akka.NET](#resolving-irequiredactortkey-within-akkanet)
- [Microsoft.Extensions.Configuration Integration](#microsoftextensionsconfiguration-integration)
    * [IConfiguration To HOCON Adapter](#iconfiguration-to-hocon-adapter)
- [Microsoft.Extensions.Logging Integration](#microsoftextensionslogging-integration)
    * [Logger Configuration Support](#logger-configuration-support)
    * [Microsoft.Extensions.Logging.ILoggerFactory Logging Support](#microsoftextensionsloggingiloggerfactory-logging-support)
    * [Serilog Support](#serilog-support)
    * [Microsoft.Extensions.Logging Log Event Filtering](#microsoftextensionslogging-log-event-filtering)
- [OpenTelemetry Trace Correlation](#opentelemetry-trace-correlation)
- [Microsoft.Extensions.Diagnostics.HealthChecks Integration](#healthchecks)
    * [Dependency Injected Health Checks](#healthcheck-di)
    * [Built-in HealthChecks](#healthcheck-builtin)
<a id="supported-packages"></a>
# Supported Packages

<a id="akkanet-core-packages"></a>
## Akka.NET Core Packages

* `Akka.Hosting` - the core `Akka.Hosting` package, needed for everything
* [`Akka.Remote.Hosting`](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/hosting/Akka.Remote.Hosting) - enables Akka.Remote configuration. Documentation can be read [here](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/hosting/Akka.Remote.Hosting/README.md)
* [`Akka.Cluster.Hosting`](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/hosting/Akka.Cluster.Hosting) - used for Akka.Cluster, Akka.Cluster.Sharding, and Akka.Cluster.Tools. Documentation can be read [here](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/hosting/Akka.Cluster.Hosting/README.md)
* [`Akka.Persistence.Hosting`](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/hosting/Akka.Persistence.Hosting) - used for adding persistence functionality to perform local database-less testing. Documentation can be read [here](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/hosting/Akka.Persistence.Hosting/README.md)

[Back to top](#akkahosting)

<a id="akka-persistence-plugins"></a>
## Akka Persistence Plugins

* [`Akka.Persistence.SqlServer.Hosting`](https://github.com/akkadotnet/Akka.Persistence.SqlServer/tree/dev/src/Akka.Persistence.SqlServer.Hosting) - used for Akka.Persistence.SqlServer support. Documentation can be read [here](https://github.com/akkadotnet/Akka.Persistence.SqlServer/blob/dev/src/Akka.Persistence.SqlServer.Hosting/README.md)
* [`Akka.Persistence.PostgreSql.Hosting`](https://github.com/akkadotnet/Akka.Persistence.PostgreSql/tree/dev/src/Akka.Persistence.PostgreSql.Hosting) - used for Akka.Persistence.PostgreSql support. Documentation can be read [here](https://github.com/akkadotnet/Akka.Persistence.PostgreSql/blob/dev/src/Akka.Persistence.PostgreSql.Hosting/README.md)
* [`Akka.Persistence.Azure.Hosting`](https://github.com/petabridge/Akka.Persistence.Azure) - used for Akka.Persistence.Azure support. Documentation can be read [here](https://github.com/petabridge/Akka.Persistence.Azure/blob/master/README.md)

[Back to top](#akkahosting)

<a id="akkamanagement-plugins"></a>
## [Akka.Management Plugins](https://github.com/akkadotnet/Akka.Management)

Useful tools for managing Akka.NET clusters running inside containerized or cloud based environment. `Akka.Hosting` is embedded in each of its packages.

[Back to top](#akkahosting)

<a id="akkamanagement-core-package"></a>
### Akka.Management Core Package

* [`Akka.Management`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management) - core module of the management utilities which provides a central HTTP endpoint for Akka management extensions. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management#akka-management)
* `Akka.Management.Cluster.Bootstrap` - used to bootstrap a cluster formation inside dynamic deployment environments. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management#akkamanagementclusterbootstrap)
  > **NOTE**
  >
  > As of version 1.0.0, cluster bootstrap came bundled inside the core `Akka.Management` NuGet package and are part of the default HTTP endpoint for `Akka.Management`. All `Akka.Management.Cluster.Bootstrap` NuGet package versions below 1.0.0 should now be considered deprecated.

[Back to top](#akkahosting)

<a id="akkadiscovery-plugins"></a>
### Akka.Discovery Plugins

* [`Akka.Discovery.AwsApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/aws/Akka.Discovery.AwsApi) - provides dynamic node discovery service for AWS EC2 environment. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/aws/Akka.Discovery.AwsApi/README.md)
* [`Akka.Discovery.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/azure/Akka.Discovery.Azure) - provides a dynamic node discovery service for Azure PaaS ecosystem. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/azure/Akka.Discovery.Azure/README.md)
* [`Akka.Discovery.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi) - provides a dynamic node discovery service for Kubernetes clusters. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi/README.md)

[Back to top](#akkahosting)

<a id="akkacoordination-plugins"></a>
### Akka.Coordination Plugins

* [`Akka.Coordination.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi) - provides a lease-based distributed lock mechanism backed by [Kubernetes CRD](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/) for [Akka.NET Split Brain Resolver](https://getakka.net/articles/clustering/split-brain-resolver.html), [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html), and [Akka.Cluster.Singleton](https://getakka.net/articles/clustering/cluster-singleton.html). Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi/README.md)
* [`Akka.Coordination.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/azure/Akka.Coordination.Azure) - provides a lease-based distributed lock mechanism backed by [Microsoft Azure Blob Storage](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blobs-overview) for [Akka.NET Split Brain Resolver](https://getakka.net/articles/clustering/split-brain-resolver.html), [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html), and [Akka.Cluster.Singleton](https://getakka.net/articles/clustering/cluster-singleton.html). Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/coordination/azure/Akka.Coordination.Azure/README.md)

[Back to top](#akkahosting)

<a id="summary"></a>
# Summary

We want to make Akka.NET something that can be instantiated more typically per the patterns often used with the Microsoft.Extensions.Hosting APIs that are common throughout .NET.

```csharp
using Akka.Hosting;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Hosting;
using Akka.Remote.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting("localhost", 8110)
        .WithClustering(new ClusterOptions(){ Roles = new[]{ "myRole" },
            SeedNodes = new[]{ Address.Parse("akka.tcp://MyActorSystem@localhost:8110")}})
        .WithActors((system, registry) =>
    {
        var echo = system.ActorOf(act =>
        {
            act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell($"{context.Self} rcv {o}");
            });
        }, "echo");
        registry.TryRegister<Echo>(echo); // register for DI
    });
});

var app = builder.Build();

app.MapGet("/", async (context) =>
{
    var echo = context.RequestServices.GetRequiredService<ActorRegistry>().Get<Echo>();
    var body = await echo.Ask<string>(context.TraceIdentifier, context.RequestAborted).ConfigureAwait(false);
    await context.Response.WriteAsync(body);
});

app.Run();
```

No HOCON. Automatically runs all Akka.NET application lifecycle best practices behind the scene. Automatically binds the `ActorSystem` and the `ActorRegistry`, another new 1.5 feature, to the `IServiceCollection` so they can be safely consumed via both actors and non-Akka.NET parts of users' .NET applications.

This should be open to extension in other child plugins, such as `Akka.Persistence.SqlServer`:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting("localhost", 8110)
        .WithClustering(new ClusterOptions()
        {
            Roles = new[] { "myRole" },
            SeedNodes = new[] { Address.Parse("akka.tcp://MyActorSystem@localhost:8110") }
        })
        .WithSqlServerPersistence(builder.Configuration.GetConnectionString("sqlServerLocal"))
        .WithShardRegion<UserActionsEntity>("userActions", s => UserActionsEntity.Props(s),
            new UserMessageExtractor(),
            new ShardOptions(){ StateStoreMode = StateStoreMode.DData, Role = "myRole"})
        .WithActors((system, registry) =>
        {
            var userActionsShard = registry.Get<UserActionsEntity>();
            var indexer = system.ActorOf(Props.Create(() => new Indexer(userActionsShard)), "index");
            registry.TryRegister<Index>(indexer); // register for DI
        });
})
```

[Back to top](#akkahosting)

<a id="dependency-injection-outside-and-inside-akkanet"></a>
# Dependency Injection Outside and Inside Akka.NET

One of the other design goals of Akka.Hosting is to make the dependency injection experience with Akka.NET as seamless as any other .NET technology. We accomplish this through two new APIs:

* The `ActorRegistry`, a DI container that is designed to be populated with `Type`s for keys and `IActorRef`s for values, just like the `IServiceCollection` does for ASP.NET services.
* The `IRequiredActor<TKey>` - you can place this type the constructor of any dependency injected resource and it will automatically resolve a reference to the actor stored inside the `ActorRegistry` with `TKey`. This is how we inject actors into ASP.NET, SignalR, gRPC, and other Akka.NET actors!

> **N.B.** The `ActorRegistry` and the `ActorSystem` are automatically registered with the `IServiceCollection` / `IServiceProvider` associated with your application.

[Back to top](#akkahosting)

<a id="registering-actors-with-the-actorregistry"></a>
## Registering Actors with the `ActorRegistry`

As part of Akka.Hosting, we need to provide a means of making it easy to pass around top-level `IActorRef`s via dependency injection both within the `ActorSystem` and outside of it.

The `ActorRegistry` will fulfill this role through a set of generic, typed methods that make storage and retrieval of long-lived `IActorRef`s easy and coherent:

* Fetch ActorRegistry from ActorSystem manually
```csharp
var registry = ActorRegistry.For(myActorSystem); 
```

* Provided by the actor builder
```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithActors((system, actorRegistry) =>
        {
            var actor = system.ActorOf(Props.Create(() => new MyActor));
            actorRegistry.TryRegister<MyActor>(actor); // register actor for DI
        });
});
```

* Obtaining the `IActorRef` manually
```csharp
var registry = ActorRegistry.For(myActorSystem); 
registry.Get<Index>(); // use in DI
```

[Back to top](#akkahosting)

<a id="injecting-actors-with-irequiredactortkey"></a>
## Injecting Actors with `IRequiredActor<TKey>`

Suppose we have a class that depends on having a reference to a top-level actor, a router, a `ShardRegion`, or perhaps a `ClusterSingleton` (common types of actors that often interface with non-Akka.NET parts of a .NET application):

```csharp
public sealed class MyConsumer
{
    private readonly IActorRef _actor;

    public MyConsumer(IRequiredActor<MyActorType> actor)
    {
        _actor = actor.ActorRef;
    }

    public async Task<string> Say(string word)
    {
        return await _actor.Ask<string>(word, TimeSpan.FromSeconds(3));
    }
}
```

The `IRequiredActor<MyActorType>` will cause the Microsoft.Extensions.DependencyInjection mechanism to resolve `MyActorType` from the `ActorRegistry` and inject it into the `IRequired<Actor<MyActorType>` instance passed into `MyConsumer`.

The `IRequiredActor<TActor>` exposes a single property:

```csharp
public interface IRequiredActor<TActor>
{
    /// <summary>
    /// The underlying actor resolved via <see cref="ActorRegistry"/> using the given <see cref="TActor"/> key.
    /// </summary>
    IActorRef ActorRef { get; }
}
```

By default, you can automatically resolve any actors registered with the `ActorRegistry` without having to declare anything special on your `IServiceCollection`:

```csharp
using var host = new HostBuilder()
  .ConfigureServices(services =>
  {
      services.AddAkka("MySys", (builder, provider) =>
      {
          builder.WithActors((system, registry) =>
          {
              var actor = system.ActorOf(Props.Create(() => new MyActorType()), "myactor");
              registry.Register<MyActorType>(actor);
          });
      });
      services.AddScoped<MyConsumer>();
  })
  .Build();
  await host.StartAsync();
```

Adding your actor and your type key into the `ActorRegistry` is sufficient - no additional DI registration is required to access the `IRequiredActor<TActor>` for that type.

[Back to top](#akkahosting)

<a id="resolving-irequiredactortkey-within-akkanet"></a>
## Resolving `IRequiredActor<TKey>` within Akka.NET

Akka.NET does not use dependency injection to start actors by default primarily because actor lifetime is unbounded by default - this means reasoning about the scope of injected dependencies isn't trivial. ASP.NET, by contrast, is trivial: all HTTP requests are request-scoped and all web socket connections are connection-scoped - these are objects have _bounded_ and typically short lifetimes.

Therefore, users have to explicitly signal when they want to use Microsoft.Extensions.DependencyInjection via [the `IDependencyResolver` interface in Akka.DependencyInjection](https://getakka.net/articles/actors/dependency-injection.html) - which is easy to do in most of the Akka.Hosting APIs for starting actors:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IReplyGenerator, DefaultReplyGenerator>();
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting(hostname: "localhost", port: 8110)
        .WithClustering(new ClusterOptions{SeedNodes = new []{ "akka.tcp://MyActorSystem@localhost:8110", }})
        .WithShardRegion<Echo>(
            typeName: "myRegion",
            entityPropsFactory: (_, _, resolver) =>
            {
                // uses DI to inject `IReplyGenerator` into EchoActor
                return s => resolver.Props<EchoActor>(s);
            },
            extractEntityId: ExtractEntityId,
            extractShardId: ExtractShardId,
            shardOptions: new ShardOptions());
});
```

The `dependencyResolver.Props<MySingletonDiActor>()` call will leverage the `ActorSystem`'s built-in `IDependencyResolver` to instantiate the `MySingletonDiActor` and inject it with all of the necessary dependencies, including `IRequiredActor<TKey>`.

[Back to top](#akkahosting)

<a id="microsoftextensionsconfiguration-integration"></a>
# Microsoft.Extensions.Configuration Integration

<a id="iconfiguration-to-hocon-adapter"></a>
## IConfiguration To HOCON Adapter

The `AddHocon` extension method can convert `Microsoft.Extensions.Configuration` `IConfiguration` into HOCON `Config` instance and adds it to the ActorSystem being configured.
* Unlike `IConfiguration`, all HOCON key names are **case sensitive**.
* **Unless enclosed inside double quotes**, all "." (period) in the `IConfiguration` key will be treated as a HOCON object key separator
* `IConfiguration` **does not support object composition**, if you declare the same key multiple times inside multiple configuration providers (JSON/environment variables/etc), **only the last one declared will take effect**.
* For environment variable configuration provider:
   * "__" (double underline) will be converted to "." (period).
   * "_" (single underline) will be converted to "-" (dash).
   * If all keys are composed of integer parseable keys, the whole object is treated as an array

__Example:__

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "akka": {
    "cluster": {
      "roles": [ "front-end", "back-end" ],
      "min-nr-of-members": 3,
      "log-info": true
    }
  }    
}
```

Environment variables:

```powershell
AKKA__ACTOR__TELEMETRY__ENABLE=true
AKKA__CLUSTER__SEED_NODES__0=akka.tcp//mySystem@localhost:4055
AKKA__CLUSTER__SEED_NODES__1=akka.tcp//mySystem@localhost:4056
AKKA__CLUSTER__SEED_NODE_TIMEOUT=00:00:05
```

Note the integer parseable key inside the seed-nodes configuration, seed-nodes will be parsed as an array. These environment variables will be parsed as HOCON settings:

```hocon
akka {
  actor {
    telemetry.enabled: on
  }
  cluster {
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    seed-node-timeout: 5s
  }
}
```

Example code:

```csharp
/*
Both appsettings.json and environment variables are combined
into HOCON configuration:

akka {
  actor.telemetry.enabled: on
  cluster {
    roles: [ "front-end", "back-end" ]
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    min-nr-of-members: 3
    seed-node-timeout: 5s
    log-info: true
  }
}
*/
var host = new HostBuilder()
    .ConfigureHostConfiguration(builder =>
    {
        // Setup IConfiguration to load from appsettings.json and
        // environment variables
        builder
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("mySystem", (builder, provider) =>
            {
                // convert IConfiguration to HOCON
                var akkaConfig = context.Configuration.GetSection("akka");
                builder.AddHocon(akkaConfig, HoconAddMode.Prepend); 
            });
    });
```

### Special Characters And Case Sensitivity

This advanced usage of the `IConfiguration` adapter is solely used for edge cases where HOCON key capitalization needs to be preserved, such as declaring serialization binding. Note that when you're using this feature, none of the keys are normalized, you will have to write all of your keys in a HOCON compatible way.

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "akka": {
    "\"Key.With.Dots\"": "Key Value",
    "cluster": {
      "roles": [ "front-end", "back-end" ],
      "min-nr-of-members": 3,
      "log-info": true
    }
  }    
}
```

Note that "Key.With.Dots" needs to be inside escaped double quotes, this is a HOCON requirement that preserves the "." (period) inside HOCON property names.

Environment variables:

```powershell
PS C:/> [Environment]::SetEnvironmentVariable('akka__actor__telemetry__enabled', 'true')
PS C:/> [Environment]::SetEnvironmentVariable('akka__actor__serialization_bindings__"System.Object"', 'hyperion')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_nodes__0', 'akka.tcp//mySystem@localhost:4055')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_nodes__1', 'akka.tcp//mySystem@localhost:4056')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_node_timeout', '00:00:05')
```

Note that:
1. All of the environment variable names are in lower case, except "System.Object" where it needs to preserve name capitalization.
2. To set serialization binding via environment variable, you have to use "." (period) instead of "__" (double underscore), this might be problematic for some shell scripts and there is no way of getting around this.

Example code:

```csharp
/*
Both appsettings.json and environment variables are combined
into HOCON configuration:

akka {
  "Key.With.Dots": Key Value
  actor {
    telemetry.enabled: on
    serialization-bindings {
      "System.Object" = hyperion
    }
  }
  cluster {
    roles: [ "front-end", "back-end" ]
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    min-nr-of-members: 3
    seed-node-timeout: 5s
    log-info: true
  }
}
*/
var host = new HostBuilder()
    .ConfigureHostConfiguration(builder =>
    {
        // Setup IConfiguration to load from appsettings.json and
        // environment variables
        builder
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("mySystem", (builder, provider) =>
            {
                // convert IConfiguration to HOCON
                var akkaConfig = context.Configuration.GetSection("akka");
                // Note the last method argument is set to false
                builder.AddHocon(akkaConfig, HoconAddMode.Prepend, false); 
            });
    });
```

[Back to top](#akkahosting)

<a id="microsoftextensionslogging-integration"></a>
# Microsoft.Extensions.Logging Integration

<a id="logger-configuration-support"></a>
## Logger Configuration Support

You can use `AkkaConfigurationBuilder` extension method called `ConfigureLoggers(Action<LoggerConfigBuilder>)` to configure how Akka.NET logger behave.

Example:
```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .ConfigureLoggers(setup =>
        {
            // Example: This sets the minimum log level
            setup.LogLevel = LogLevel.DebugLevel;
            
            // Example: Clear all loggers
            setup.ClearLoggers();
            
            // Example: Add the default logger
            // NOTE: You can also use setup.AddLogger<DefaultLogger>();
            setup.AddDefaultLogger();
            
            // Example: Add the ILoggerFactory logger
            // NOTE:
            //   - You can also use setup.AddLogger<LoggerFactoryLogger>();
            //   - To use a specific ILoggerFactory instance, you can use setup.AddLoggerFactory(myILoggerFactory);
            setup.AddLoggerFactory();
            
            // Example: Adding a serilog logger
            setup.AddLogger<SerilogLogger>();
        })
        .WithActors((system, registry) =>
        {
            var echo = system.ActorOf(act =>
            {
                act.ReceiveAny((o, context) =>
                {
                    Logging.GetLogger(context.System, "echo").Info($"Actor received {o}");
                    context.Sender.Tell($"{context.Self} rcv {o}");
                });
            }, "echo");
            registry.TryRegister<Echo>(echo); // register for DI
        });
});
```

A complete code sample can be viewed [here](https://github.com/akkadotnet/Akka.Hosting/tree/dev/src/Examples/Akka.Hosting.LoggingDemo).

Exposed properties are:
- `LogLevel`: Configure the Akka.NET minimum log level filter, defaults to `InfoLevel`
- `LogConfigOnStart`: When set to true, Akka.NET will log the complete HOCON settings it is using at start up, this can then be used for debugging purposes.

Currently supported logger methods:
- `ClearLoggers()`: Clear all registered logger types.
- `AddLogger<TLogger>()`: Add a logger type by providing its class type.
- `AddDefaultLogger()`: Add the default Akka.NET console logger.
- `AddLoggerFactory()`: Add the new `ILoggerFactory` logger.

[Back to top](#akkahosting)

<a id="microsoftextensionsloggingiloggerfactory-logging-support"></a>
## Microsoft.Extensions.Logging.ILoggerFactory Logging Support

You can now use `ILoggerFactory` from Microsoft.Extensions.Logging as one of the sinks for Akka.NET logger. This logger will use the `ILoggerFactory` service set up inside the dependency injection `ServiceProvider` as its sink.

[Back to top](#akkahosting)

<a id="serilog-support"></a>
## Serilog Message Formatting Support

If you're interested in using [Akka.Logger.Serilog](https://github.com/akkadotnet/Akka.Logger.Serilog), you can set Akka.NET's default logger and log message formatter to allow for Serilog's semantic logging to be enabled by default:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .ConfigureLoggers(setup =>
        {
            // Example: This sets the minimum log level
            setup.LogLevel = LogLevel.DebugLevel;
            
            // Example: Clear all loggers
            setup.ClearLoggers();
            
            // Add Serilog
            setup.AddLogger<SerilogLogger>();
            
            // use the default SerilogFormatter everywhere
            setup.WithDefaultLogMessageFormatter<SerilogLogMessageFormatter>();
        })
        .WithActors((system, registry) =>
        {
            var echo = system.ActorOf(act =>
            {
                act.ReceiveAny((o, context) =>
                {
                    Logging.GetLogger(context.System, "echo").Info($"Actor received {o}");
                    context.Sender.Tell($"{context.Self} rcv {o}");
                });
            }, "echo");
            registry.TryRegister<Echo>(echo); // register for DI
        });
});
```

This will eliminate the need to have to do `Context.GetLogger<SerilogLoggingAdapter>()` everywhere you want to use it.

[Back to top](#akkahosting)

<a id="microsoftextensionslogging-log-event-filtering"></a>
## Microsoft.Extensions.Logging Log Event Filtering

There will be two log event filters acting on the final log input, the Akka.NET `akka.loglevel` setting and the `Microsoft.Extensions.Logging` settings, make sure that both are set correctly or some log messages will be missing.

To set up the `Microsoft.Extensions.Logging` log filtering, you will need to edit the `appsettings.json` file. Note that we also set the `Akka` namespace to be filtered at debug level in the example below.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Akka": "Debug"
    }
  }
}
```

[Back to top](#akkahosting)

<a id="opentelemetry-trace-correlation"></a>
# OpenTelemetry Trace Correlation

Akka.NET processes log events asynchronously, which means `Activity.Current` does not flow across actor mailbox boundaries. To preserve trace correlation, Akka.Hosting captures the `ActivityContext` at log creation time and includes it in the log state. The `AkkaTraceContextProcessor` then applies that context to OpenTelemetry `LogRecord`s so exporters can correlate logs with traces.

Minimal setup:

```csharp
using Akka.Hosting;
using Akka.Hosting.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("my-service"));

    // Register before exporters
    options.AddAkkaTraceCorrelation();

    // Add OTLP exporter if you have not configured it elsewhere.
    // Your mileage may vary; use the OpenTelemetry configuration that fits your app.
    options.AddOtlpExporter();
});

builder.Services.AddAkka("MySystem", configBuilder =>
{
    configBuilder.ConfigureLoggers(setup =>
    {
        setup.ClearLoggers();
        setup.AddLoggerFactory();
    });
});
```

See the demo in `src/Examples/Akka.Hosting.OpenTelemetry.AppHost` and `src/Examples/Akka.Hosting.OpenTelemetry.Demo` for a working Aspire setup.

[Back to top](#akkahosting)

<a id="filtering-logs"></a>
## Filtering Logs In Akka.NET

In Akka.NET 1.5.21, we introduced [log filtering for log messages based on the LogSource or the content of a log message](https://getakka.net/articles/utilities/logging.html#filtering-log-messages). Depending on your coding style, you can use this feature in Akka.Hosting in several ways.

1. Using The `LoggerConfigBuilder.WithLogFilter()` method.

   The `LoggerConfigBuilder.WithLogFilter()` method lets you set up the `LogFilterBuilder`

   ```csharp
   builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
   {
       configurationBuilder
           .ConfigureLoggers(loggerConfigBuilder =>
           {
               loggerConfigBuilder.WithLogFilter(filterBuilder =>
               {
                   filterBuilder.ExcludeMessageContaining("Test");
               });
           });
   });
   ```

2. Setting the `loggerConfigBuilder.LogFilterBuilder` property directly.

   ```csharp
   builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
   {
       configurationBuilder
           .ConfigureLoggers(loggerConfigBuilder =>
           {
               loggerConfigBuilder.LogFilterBuilder = new LogFilterBuilder();
               loggerConfigBuilder.LogFilterBuilder.ExcludeMessageContaining("Test");
           });
   });
   ```

[Back to top](#akkahosting)

<a id="healthchecks"></a>
## Microsoft.Extensions.Diagnostics.HealthChecks Integration

We've recently deprecated [Akka.HealthChecks](https://github.com/petabridge/akkadotnet-healthcheck) in favor of a simpler, more configurable solution that is built directly into Akka.Hosting: `IAkkaHealthCheck` and `WithAkkaHealthCheck`:

```csharp
 builder
    .WithActorSystemLivenessCheck() // have to opt-in to the built-in health check
    .WithHealthCheck("FooActor alive", async (system, registry, cancellationToken) =>
{
    /*
     * N.B. CancellationToken is set by the call to MSFT.EXT.DIAGNOSTICS.HEALTHCHECK,
     * so that value could be "infinite" by default.
     *
     * Therefore, it might be a really, really good idea to guard this with a non-infinite
     * timeout via a LinkedCancellationToken here.
     */
    try
    {
        var fooActor = await registry.GetAsync<FooActor>(cancellationToken);

        try
        {
            var r = await fooActor.Ask<ActorIdentity>(new Identify("foo"), cancellationToken: cancellationToken);
            if (r.Subject.IsNobody())
                return HealthCheckResult.Unhealthy("FooActor was alive but is now dead");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Degraded("FooActor found but non-responsive", e);
        }
    }
    catch (Exception e2)
    {
        return HealthCheckResult.Unhealthy("FooActor not found in registry", e2);
    }

    return HealthCheckResult.Healthy("fooActor found and responsive");
});
```

These health checks and any other you register using one of the `WithHealthCheck` overloads on the `AkkaConfigurationBuilder` will automatically be registered with the [`Microsoft.Extensions.Diagnostics.HealthCheckService`](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) and will be called just like any other ASP.NET Core, Entity Framework, etc health check.

[Back to top](#akkahosting)

<a id="healthcheck-di"></a>
### Dependency Injected Health Checks

As of version 1.5.51, Akka.Hosting supports dependency injection for health checks. You can create custom health check classes that implement `IAkkaHealthCheck` and have dependencies injected from the DI container:

```csharp
// Define a custom health check with DI support
public class MyHealthCheckWithDependencies : IAkkaHealthCheck
{
    private readonly ILogger<MyHealthCheckWithDependencies> _logger;
    private readonly IMyService _myService;

    public MyHealthCheckWithDependencies(
        ILogger<MyHealthCheckWithDependencies> logger,
        IMyService myService)
    {
        _logger = logger;
        _myService = myService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        AkkaHealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Running health check with DI");
            var isHealthy = await _myService.CheckServiceHealthAsync(cancellationToken);

            return isHealthy
                ? HealthCheckResult.Healthy("Service is healthy")
                : HealthCheckResult.Unhealthy("Service is not healthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }
}

// Register the health check using the generic WithHealthCheck<T>() method
builder
    .WithActorSystemLivenessCheck()
    .WithHealthCheck<MyHealthCheckWithDependencies>(
        name: "MyServiceHealth",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "service" },
        timeout: TimeSpan.FromSeconds(5));
```

The health check type will be resolved from the DI container when the health check is executed, allowing you to leverage constructor injection for any dependencies your health check needs. The health check instance itself doesn't need to be registered in DI - Akka.Hosting will automatically resolve it using `ActivatorUtilities.GetServiceOrCreateInstance<T>()`.

[Back to top](#akkahosting)

<a id="healthcheck-builtin"></a>
### Built-in Health Checks

> ![NOTE]
> All Akka.NET health checks will be tagged with the `akka` tag, so they [can easily be filtered via the health check endpoints](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0#filter-health-checks).

Akka.Hosting and its other packages ship with some built-in health checks:

* `WithActorSystemLivenessCheck()` - a liveness probe that will fail if the `ActorSystem` is terminated. Generally, Akka.Hosting will try to shut down your process anyway if the `ActorSystem` dies.
* `WithAkkaClusterReadinessCheck` - if you are an Akka.Cluster user, this health check will return `HealthStatus.Unhealthy` until you successfully join a cluster - that way you can stop load-balancers and other devices from routing traffic to this node until it has access to the cluster. This readiness check is also tagged with the `ready` tag for filtering purposes.
* **Akka.Persistence Health Checks** - verify that persistence plugins (journals and snapshot stores) are properly initialized and accessible. These health checks use the built-in Akka.Persistence health check APIs to validate plugin connectivity and functionality. Health checks are tagged with `akka`, `persistence`, and either `journal` or `snapshot-store` for filtering purposes.

#### Configuring Persistence Health Checks

You can add health checks for your persistence plugins using the `.WithHealthCheck()` method when configuring journals and snapshot stores:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        // Journal with health check
        .WithJournal(
            new SqlServerJournalOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            journal => journal
                .AddWriteEventAdapter<MyAdapter>("adapter", new[] { typeof(MyEvent) })
                .WithHealthCheck(
                    unHealthyStatus: HealthStatus.Degraded,
                    name: "sql-journal"))

        // Snapshot store with health check
        .WithSnapshot(
            new SqlServerSnapshotOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            snapshot => snapshot
                .WithHealthCheck(
                    unHealthyStatus: HealthStatus.Degraded,
                    name: "sql-snapshot"));
});
```

You can also configure both journal and snapshot health checks together:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithJournalAndSnapshot(
            new SqlServerJournalOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            new SqlServerSnapshotOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            journal => journal.WithHealthCheck(),
            snapshot => snapshot.WithHealthCheck());
});
```

The health checks will automatically:
- Verify the persistence plugin is configured correctly
- Test connectivity to the underlying storage (database, cloud storage, etc.)
- Report `Healthy` when the plugin is operational
- Report `Degraded` or `Unhealthy` (configurable) when issues are detected

[Back to top](#akkahosting)
