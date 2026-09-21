---
uid: hosting-dependency-injection
title: Dependency Injection Outside and Inside Akka.NET
---

# Dependency Injection Outside and Inside Akka.NET

One of the other design goals of Akka.Hosting is to make the dependency injection experience with Akka.NET as seamless as any other .NET technology. We accomplish this through two new APIs:

* The `ActorRegistry`, a DI container that is designed to be populated with `Type`s for keys and `IActorRef`s for values, just like the `IServiceCollection` does for ASP.NET services.
* The `IRequiredActor<TKey>` - you can place this type the constructor of any dependency injected resource and it will automatically resolve a reference to the actor stored inside the `ActorRegistry` with `TKey`. This is how we inject actors into ASP.NET, SignalR, gRPC, and other Akka.NET actors!

> [!NOTE]
> The `ActorRegistry` and the `ActorSystem` are automatically registered with the `IServiceCollection` / `IServiceProvider` associated with your application.

## Registering Actors with the ActorRegistry

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

## Resolving `IRequiredActor<TKey>` Within Akka.NET

Akka.NET does not use dependency injection to start actors by default primarily because actor lifetime is unbounded by default - this means reasoning about the scope of injected dependencies isn't trivial. ASP.NET, by contrast, is trivial: all HTTP requests are request-scoped and all web socket connections are connection-scoped - these are objects have *bounded* and typically short lifetimes.

Therefore, users have to explicitly signal when they want to use Microsoft.Extensions.DependencyInjection via [the `IDependencyResolver` interface in Akka.DependencyInjection](xref:dependency-injection) - which is easy to do in most of the Akka.Hosting APIs for starting actors:

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
