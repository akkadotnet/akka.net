# Getting Started with Akka.NET v1.6 (Beta)

The [Akka.NET website](https://getakka.net) is frozen on v1.5 until v1.6 stabilizes. This page
is the guide for the v1.6 beta until the website catches up. It's current against the `dev`
branch. Read it in about ten minutes.

## What's in v1.6

* **.NET 10 only.** Every project in this repo targets `net10.0` - libraries, tests, and
  samples alike. The one exception is the Roslyn source generator project
  (`Akka.Serialization.V2.Generators`), which targets `netstandard2.0` because analyzers have
  to. v1.5's net8.0/net6.0/net48/netstandard2.0 multi-targeting is gone. Install the
  [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json`) before
  you build or run a v1.6 app.
* **Akka.Hosting ships from this repo now.** `Akka.Hosting`, `Akka.Cluster.Hosting`,
  `Akka.Remote.Hosting`, and `Akka.Persistence.Hosting` moved from the separate Akka.Hosting
  repo into `src/contrib/hosting`. Same package ids, namespaces, and APIs - one repo, one
  version number, one release process.
* **Artery transport.** A new TCP remoting transport, built on Akka.Streams, runs beside
  classic DotNetty remoting. Off by default. See [Enabling Artery](#enabling-artery) below.
* **Source-generated V2 serializer.** `Akka.Serialization.V2` adds a Roslyn source generator
  for MessagePack-backed serializers: a compile-time schema, no reflection, AOT-safe
  registration. See
  [Source-generated serializer](#source-generated-serializer-akkaserializationv2) below.
* **Native AOT support, in progress.** A local `ActorSystem` and a basic Akka.Hosting app
  publish and run clean under Native AOT in CI. Classic remoting does not work under AOT at
  all; Artery is the target transport for Remote under AOT, but only a manual probe has tried
  it so far. Akka.Cluster, Akka.Persistence, and cluster tools aren't supported under AOT yet.
  See [Native AOT and trimming](#native-aot-and-trimming) below.

Every breaking change in this cycle is tracked in
[`BREAKING_CHANGES_V1.6.md`](BREAKING_CHANGES_V1.6.md). Read it before you upgrade an
existing v1.5 app.

## Getting Started with Akka.Hosting

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAkka("my-system", (akkaBuilder, _) =>
{
    akkaBuilder.WithActors((system, registry) =>
    {
        var echo = system.ActorOf(Props.Create(() => new EchoActor()), "echo");
        registry.Register<EchoActor>(echo);
    });
});

var host = builder.Build();
await host.StartAsync();

var echoRef = await host.Services.GetRequiredService<IRequiredActor<EchoActor>>().GetAsync();
var reply = await echoRef.Ask<string>("hello", TimeSpan.FromSeconds(3));
Console.WriteLine(reply); // echo:hello

await host.StopAsync();

public sealed class EchoActor : ReceiveActor
{
    public EchoActor()
    {
        ReceiveAny(msg => Sender.Tell($"echo:{msg}"));
    }
}
```

Add the package first:

```xml
<PackageReference Include="Akka.Hosting" Version="*-beta*" />
```

`AddAkka` extends `IServiceCollection`. `WithActors` starts actors and registers each one in
the `ActorRegistry`, so any other part of the DI container can resolve it later through
`IRequiredActor<T>`. For clustering and sharding, see the larger samples under
`src/examples/Hosting/` in this repo.

## Native AOT and Trimming

CI publishes two canary apps on every PR, with `PublishAot=true`, and fails the build on any
new trimming/AOT warning: `src/aot/Akka.AOT.App` (a plain `ActorSystem`) and
`src/aot/Akka.Hosting.AOT.App` (an Akka.Hosting app). Both run clean today.

Everything beyond that is unverified by CI. Classic DotNetty remoting does not work under
Native AOT - it loads its transport by type name, and that lookup fails at startup.
[Artery](#enabling-artery) is the target transport for Remote under AOT; a manual probe has
round-tripped a remote `Ask` over Artery under AOT, but no CI canary covers it yet. Treat
Akka.Cluster, Akka.Persistence, and cluster tools (ClusterClient, PubSub, Singleton) as
unsupported under AOT for now - several of their own settings (the downing provider, a journal
plugin class, event adapters, the DData durable store) still resolve a type by name,
unguarded, regardless of the switch below.

Publishing under Native AOT turns off a feature switch, `Akka.DynamicTypeLoading`:

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <PublishAot>true</PublishAot>
</PropertyGroup>

<ItemGroup>
  <RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="false" Trim="true" />
</ItemGroup>
```

When you consume Akka through a NuGet package (not a `ProjectReference`, as above), this
switch flips off by itself whenever `PublishAot` or `PublishTrimmed` is `true` - you don't add
it by hand.

With the switch off, HOCON can no longer resolve a logger, serializer, or extension by type
name. Three `Setup` types replace that: `LoggerSetup`, `SerializationSetup`, and
`ExtensionsSetup`. Combine them - with `BootstrapSetup` too, if you need it - through
`ActorSystemSetup`:

```csharp
var setup = ActorSystemSetup.Create(BootstrapSetup.Create())
    .And(LoggerSetup.Create(Props.Create<ConsoleLogger>()))
    .And(ExtensionsSetup.Create(CounterExtensionProvider.Instance))
    .And(SerializationSetup.Create(system => ImmutableHashSet.Create(
        SerializerDetails.Create(
            "counter",
            new CounterMessageSerializer(system),
            ImmutableHashSet.Create(typeof(CounterMessage))))));

var actorSystem = ActorSystem.Create("setup-apis-demo", setup);
```

Two things this doesn't cover: stream refs need the switch left **on**
([#8667](https://github.com/akkadotnet/akka.net/issues/8667)), and a serializer that writes a
manifest has to map it back to a type itself - override `FromBinary(byte[], string)`, or
derive from `SerializerWithStringManifest`, instead of relying on `Type.GetType` from the
manifest string.

The default JSON serializer, Newtonsoft.Json, is reflection-based and gets trimmed away when
the switch is off. Use the
[source-generated serializer](#source-generated-serializer-akkaserializationv2) for your own
message types instead - it's designed for AOT (no reflection, an AOT-safe `CreateSetup()`),
though no CI canary exercises it under Native AOT yet either.

To opt back into reflection-based loading (and bring back its `IL2026`/`IL3050` warnings):

```xml
<RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="true" />
```

Full details, including everything not yet supported:
[Native AOT and Trimming](docs/articles/deployment/native-aot.md).

## Source-Generated Serializer (Akka.Serialization.V2)

```xml
<PackageReference Include="Akka.Serialization.V2" Version="*-beta*" />
```

One package reference. No separate generator package - the generator ships inside
`Akka.Serialization.V2` itself and NuGet wires it in automatically.

```csharp
using Akka.Actor;
using Akka.Serialization.V2;

var setup = OrderSerializer.CreateRegistration().CreateSetup();
var bootstrap = BootstrapSetup.Create().And(setup);
var system = ActorSystem.Create("order-system", bootstrap);

// A protocol interface groups the message types one serializer owns.
public interface IOrderProtocol
{
}

// Index every field with [AkkaField(n)]. A missing or duplicate index is a build error.
[AkkaSerializable(Manifest = "submit-order-v1")]
public sealed record SubmitOrder(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] decimal Total) : IOrderProtocol;

// A sealed partial class. The generator fills in CreateRegistration(). Name and id are
// always explicit - ids 1-100 are reserved for Akka's own built-in serializers.
[AkkaSerializer<IOrderProtocol>("order-serializer", 120001)]
public sealed partial class OrderSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
```

To register several generated serializers at once, use
`SerializerRegistration.CreateSetup(params SerializerRegistration[])` and merge the result into
`ActorSystemSetup` the same way. A generated serializer also satisfies Akka's classic
`Serializer` contract, so you can register it through HOCON instead, exactly like a
hand-written one - Akka.Remote registers its own generated Artery control-message serializer
this way today.

One message type can belong to only one serializer. The generator's analyzer catches a
conflict at compile time (for example, two serializers binding the same protocol interface);
`SerializerRegistration.CreateSetup` catches the same conflict at startup when registrations
come from more than one assembly. There's no `Akka.Hosting` helper for this yet - compose
through `SerializationSetup` and `ActorSystemSetup`, or HOCON, until one ships.

Full details: [Source-Generated MessagePack Serialization](docs/articles/serialization/source-generated-serialization.md).

## Enabling Artery

Artery is off by default. Classic DotNetty remoting is unaffected either way:

```hocon
akka {
  actor.provider = remote
  remote.artery {
    enabled = on
    canonical {
      hostname = "localhost"
      port = 25520
    }
  }
}
```

`tcp` is the only transport implemented today (and the default once Artery is enabled) - other
values fail at startup. Classic remoting (`akka.tcp://...`) and Artery (`akka://...`) are not
wire-compatible, so every node in a cluster must run the same transport. There's no
`Akka.Hosting` helper for Artery yet; configure it through HOCON as shown above. Artery is
still experimental and under active development.

## Getting Help and Reporting Beta Issues

Found a bug in the v1.6 beta, or something in this guide doesn't match what you see?
[Open a GitHub issue](https://github.com/akkadotnet/akka.net/issues). For questions, use
[GitHub Discussions](https://github.com/akkadotnet/akka.net/discussions) or
[Akka.NET on Discord](https://discord.gg/GSCfPwhbWP).
