---
uid: hosting-configuration-builder
title: The AkkaConfigurationBuilder API
---

# The AkkaConfigurationBuilder API

We want to make Akka.NET something that can be instantiated more typically per the patterns often used with the Microsoft.Extensions.Hosting APIs that are common throughout .NET.

The `AddAkka` extension method on `IServiceCollection` is the entry point into Akka.Hosting - it hands you an `AkkaConfigurationBuilder` that you use to configure and start your `ActorSystem`:

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

## Other AkkaConfigurationBuilder Methods

Beyond `WithActors`, the `AkkaConfigurationBuilder` exposes several other methods for advanced configuration scenarios:

* `AddHocon` - merges a HOCON `Config` object, or a `Microsoft.Extensions.Configuration` `IConfiguration` section, into the `ActorSystem` configuration. See [Microsoft.Extensions.Configuration Integration](xref:hosting-configuration) for details.
* `AddSetup` - adds an Akka.NET `Setup` object, such as `BootstrapSetup` or `ServiceProviderSetup`, to the `ActorSystem` startup pipeline for programmatic configuration.
* `WithActorRefProvider` - overrides the `ActorRefProvider` used by the `ActorSystem` (for example, forcing `ActorRefProvider.Local` in a test host that would otherwise default to `ActorRefProvider.Cluster`).

For dependency injection with the `ActorRegistry` and `IRequiredActor<TKey>`, see [Dependency Injection Outside and Inside Akka.NET](xref:hosting-dependency-injection).
