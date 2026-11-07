# Getting Started with Akka.NET v1.6 (Beta)

The [Akka.NET website](https://getakka.net) is frozen on v1.5 until v1.6 stabilizes. This page
is the guide for the v1.6 beta until the website catches up. It's current against the `dev`
branch.

**The v1.6 betas are on NuGet.** `1.6.0-beta1` through `1.6.0-beta5` are published, so the
`Version="1.6.0-beta*"` references below restore. Pin an exact beta rather than the wildcard if
you need a reproducible build.

## What's in v1.6

* **.NET 10 only.** Every package targets `net10.0` - libraries, tests, and samples alike. The
  one exception is the Roslyn source generator project (`Akka.Serialization.V2.Generators`),
  which targets `netstandard2.0` because analyzers have to. v1.5's
  net8.0/net6.0/net48/netstandard2.0 multi-targeting is gone. Install the
  [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json`) before
  you build or run a v1.6 app.
* **Akka.Hosting ships from this repo now.** `Akka.Hosting`, `Akka.Cluster.Hosting`,
  `Akka.Remote.Hosting`, `Akka.Persistence.Hosting`, `Akka.Hosting.TestKit`, and
  `Akka.Hosting.TestKit.Xunit2` moved from the separate Akka.Hosting repo into
  `src/contrib/hosting`. Same package ids and namespaces. Reference every `Akka.Hosting.*`
  package at the same version as `Akka.*` now - hosting-only versions are gone - and bring
  `Microsoft.Extensions.*` up to 10.0.0, the new minimum. Hosting itself changed too: extensions
  now register through `ExtensionsSetup` instead of writing type names into `akka.extensions`
  ([#8649](https://github.com/akkadotnet/akka.net/pull/8649) in the ledger).
* **Artery transport.** A new TCP remoting transport, built on Akka.Streams, runs beside
  classic DotNetty remoting. Off by default. See [Enabling Artery](#enabling-artery) below.
* **Source-generated V2 serializer.** `Akka.Serialization.V2` adds a Roslyn source generator
  for MessagePack-backed serializers: a compile-time schema, no reflection, AOT-safe
  registration. See
  [Source-generated serializer](#source-generated-serializer-akkaserializationv2) below.
* **Native AOT support, in progress.** A local `ActorSystem` and a basic Akka.Hosting app
  publish and run clean under Native AOT in CI. Classic remoting does not work under AOT at
  all; Artery is the target transport for Remote under AOT, but only a manual probe has tried
  it so far. Akka.Persistence runs under AOT through the Akka.Persistence.Hosting builders; Akka.Cluster
  and cluster tools aren't supported under AOT yet. See
  [Native AOT and trimming](#native-aot-and-trimming) below.

Every breaking change in this cycle is tracked in
[`BREAKING_CHANGES_V1.6.md`](BREAKING_CHANGES_V1.6.md). Read it before you upgrade an
existing v1.5 app.

## Upgrading from 1.5

A third-party plugin built against 1.5 - a persistence plugin, Akka.Management, a logger -
can hit a binary break on 1.6. Wait for its own 1.6 build rather than forcing the old one in.
Before a rolling upgrade of a live cluster, read every `Wire` row in
[`BREAKING_CHANGES_V1.6.md`](BREAKING_CHANGES_V1.6.md) - those are the changes that can break
compatibility between a 1.5 node and a 1.6 node while both are up.

## Getting Started with Akka.Hosting

```xml
<PackageReference Include="Akka.Hosting" Version="1.6.0-beta*" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
```

`Akka.Hosting` depends on `Microsoft.Extensions.Hosting.Abstractions` only; add the full
`Microsoft.Extensions.Hosting` package yourself for `Host.CreateApplicationBuilder`, same as
any other generic-host app.

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

`AddAkka` extends `IServiceCollection`. `WithActors` runs your callback once, after the
`ActorSystem` starts; register each actor in the `ActorRegistry` yourself, so any other part
of the DI container can resolve it later through `IRequiredActor<T>`. For clustering and
sharding, see the larger samples under `src/examples/Hosting/` in this repo.

## Native AOT and Trimming

CI publishes two canary apps on every PR, with `PublishAot=true`, and fails the build on any
new trimming/AOT warning: `src/aot/Akka.AOT.App` (a plain `ActorSystem`) and
`src/aot/Akka.Hosting.AOT.App` (an Akka.Hosting app). Both run clean today.

Everything beyond that is unverified by CI. Classic DotNetty remoting does not work under
Native AOT - it loads its transport by type name, and that lookup fails at startup.
[Artery](#enabling-artery) is the target transport for Remote under AOT; a manual probe has
round-tripped a remote `Ask` over Artery under AOT, but no CI canary covers it yet. Treat
Akka.Cluster and cluster tools (ClusterClient, PubSub, Singleton) as
unsupported under AOT for now - several of their own settings (the downing provider, the DData
durable store) still resolve a type by name, unguarded, regardless of the switch below.

Akka.Persistence works under AOT when you configure it through the Akka.Persistence.Hosting
builders (`WithJournal`, `WithSnapshot`, `WithInMemoryJournal` and the others): they hand core the
plugins, event adapters and read journals as code. A third-party persistence plugin has to opt in; if
you write one, read
[Making Your Persistence Plugin Native AOT Ready](docs/articles/persistence/custom-persistence-provider.md#making-your-persistence-plugin-native-aot-ready).
`src/aot/Akka.Persistence.AOT.App` is the CI canary for it.

Publishing under Native AOT turns off a feature switch, `Akka.DynamicTypeLoading`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

When you consume Akka through a NuGet package, that alone is enough - the switch flips off by
itself whenever `PublishAot` or `PublishTrimmed` is `true`. If you reference Akka by
`ProjectReference` instead (as this repo's own AOT canary apps do), also add it by hand:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="false" Trim="true" />
</ItemGroup>
```

With the switch off, HOCON can no longer resolve *your own* logger, serializer, or extension
by type name - Akka's built-in loggers, serializers, and schedulers still resolve from a
built-in table either way. Three `Setup` types replace what the type name used to do:
`LoggerSetup`, `SerializationSetup`, and `ExtensionsSetup`. Combine them - with
`BootstrapSetup` too, if you need it - through `ActorSystemSetup`:

```csharp
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Event;
using Akka.Serialization;

// ConsoleLogger, CounterExtensionProvider, CounterMessageSerializer, and CounterMessage are
// ordinary types you write yourself: a logger actor, an ExtensionIdProvider, a hand-written
// Serializer, and the message it serializes.
var setup = ActorSystemSetup.Create(BootstrapSetup.Create())
    .And(LoggerSetup.Create(Props.Create<ConsoleLogger>()))
    .And(ExtensionsSetup.Create(CounterExtensionProvider.Instance))
    .And(SerializationSetup.Create(extendedSystem => ImmutableHashSet.Create(
        SerializerDetails.Create(
            "counter",
            new CounterMessageSerializer(system),
            ImmutableHashSet.Create(typeof(CounterMessage))))));

var actorSystem = ActorSystem.Create("setup-apis-demo", setup);
```

Put every serializer into that one `SerializationSetup` call. `ActorSystemSetup` keeps only
one setup per type, so a second `.And(SerializationSetup.Create(...))` replaces the first
instead of adding to it.

Two more things this doesn't cover: stream refs need the switch left **on**
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
<PackageReference Include="Akka.Serialization.V2" Version="1.6.0-beta*" />
```

No separate generator package - the generator ships inside `Akka.Serialization.V2` itself and
NuGet wires it in automatically.

```csharp
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;

var setup = SerializationSetup.Create(extendedSystem => ImmutableHashSet.Create(OrderSerializer.CreateSerializerDetails(extendedSystem)));
var bootstrap = BootstrapSetup.Create().And(setup);
var system = ActorSystem.Create("order-system", bootstrap);

// A protocol interface groups the message types one serializer owns.
public interface IOrderProtocol
{
}

// Index every field with [AkkaField(n)]. Two fields sharing an index is a build error
// (AKKASG005); on a positional record, a constructor parameter with no matching [AkkaField]
// is too (AKKASG026). Gaps in the numbering are fine. A public stored property with no
// [AkkaField] warns (AKKASG046) - it is not serialized - and [AkkaField] on a type without
// [AkkaSerializable] warns too (AKKASG047). Both fail a warnings-as-errors build.
[AkkaSerializable(Manifest = "submit-order-v1")]
public sealed record SubmitOrder(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] decimal Total) : IOrderProtocol;

// A sealed partial class. The generator fills in CreateSerializerDetails(). Name and id are
// always explicit - ids 1-100 are reserved for Akka's own built-in serializers.
[AkkaSerializer<IOrderProtocol>("order-serializer", 120001)]
public sealed partial class OrderSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
```

To register several generated serializers, return their `SerializerDetails` in the same
`SerializationSetup.Create` factory. A generated serializer also satisfies Akka's classic
`Serializer` contract, so you can register it through HOCON instead.

Using Akka.Hosting? Pass the generated factory directly:

```csharp
akkaBuilder.WithCustomSerializer(OrderSerializer.CreateSerializerDetails);
```

One message type can belong to only one serializer. The generator's analyzer catches a
conflict at compile time (for example, two serializers binding the same protocol interface).

Full details: [Source-Generated MessagePack Serialization](docs/articles/serialization/source-generated-serialization.md).

## Enabling Artery

Artery is experimental. Don't use it in production - Akka.NET logs that warning itself the
moment it starts. It's off by default, and needs the `Akka.Remote` package either way, same as
classic remoting:

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
values fail at startup. One `ActorSystem` uses one transport: classic (`akka.tcp://...`) and
Artery (`akka://...`) are not wire-compatible, so every node in a cluster must run the same
one. That also changes every address and seed node you write down - classic's
`akka.tcp://MySystem@host:2552` becomes Artery's `akka://MySystem@host:25520`, a different
scheme and a different default port. With Akka.Hosting, `WithArteryRemoting` enables Artery
and accepts host, port, and optional TLS settings, as shown below. It preserves an existing
Cluster provider; a local or unspecified provider becomes Remote.

### TLS and Mutual TLS

Artery supports server-only TLS and mutual TLS (mTLS) through immutable settings configured
in code. An `ArteryTlsSetup` enables TLS for every inbound and outbound connection, including
ordinary, control, and large-message channels. Omitting TLS settings uses plaintext TCP;
a failed TLS handshake never falls back to plaintext. TLS keeps the `akka://` address scheme.

The TLS APIs in this section require a build containing [#8791](https://github.com/akkadotnet/akka.net/pull/8791)
and its prerequisite PRs; `1.6.0-beta6` and earlier do not include them.

For mTLS, every node needs a certificate with an accessible private key, suitable for both
client and server authentication. The following console example needs `Akka.Remote`, a
`node.pfx` file, and its password in `NODE_CERT_PASSWORD`. It uses the operating system's
certificate-chain trust, matching DotNetty's default validation with hostname checking off.
Install your private CA in the operating system trust store if you use one.

```csharp
using System;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;

using var certificate = TlsCertificateLoader.LoadPkcs12FromFile(
    "node.pfx", Environment.GetEnvironmentVariable("NODE_CERT_PASSWORD"),
    X509KeyStorageFlags.DefaultKeySet);
var peerPolicy = TlsPeerPolicy.SystemTrust();
var tls = ArteryTlsSettings.Mutual(certificate, peerPolicy);
var config = ConfigurationFactory.ParseString("""
    akka.actor.provider = remote
    akka.remote.artery {
        enabled = on
        canonical.hostname = "127.0.0.1"
        canonical.port = 25520
    }
    """);
var setup = ActorSystemSetup.Create(
    BootstrapSetup.Create().WithConfig(config), new ArteryTlsSetup(tls));
var system = ActorSystem.Create("MySystem", setup);
try
{
    Console.WriteLine("TLS node running. Press Enter to stop.");
    Console.ReadLine();
}
finally
{
    await system.Terminate();
}
```

Use an address reachable by the other nodes in place of the loopback hostname. Keep the
certificate alive until the actor system has terminated, as above. For server-only TLS,
replace `Mutual(certificate, peerPolicy)` with `ServerOnly(certificate, peerPolicy)` on each
node: each listener still presents its server certificate, while incoming clients do not
have to present one.

For Akka.Hosting, reference `Akka.Remote.Hosting`, add `using Akka.Remote.Hosting;`, and
pass the same `tls` profile inside your `AddAkka` callback:

```csharp
akkaBuilder.WithArteryRemoting(options =>
{
    options.HostName = "127.0.0.1";
    options.Port = 25520;
    options.Tls = tls;
});
```

### Migrating DotNetty TLS Settings

Artery does not read `akka.remote.dot-netty.tcp.ssl`, `DotNettySslSetup`, or the DotNetty
`WithRemoting` SSL options. Replace them with certificate loading and an explicit peer
policy. For example, this classic configuration's trust and mTLS settings map to the
`SystemTrust()` and `Mutual(...)` example above:

```hocon
akka.remote.dot-netty.tcp {
    enable-ssl = on
    ssl {
        certificate.path = "node.pfx"
        certificate.password = "your-existing-password"
        suppress-validation = off
        require-mutual-authentication = on
        validate-certificate-hostname = off
    }
}
```

| DotNetty setting or API | Artery TLS equivalent |
| --- | --- |
| `enable-ssl = on` | Supply `ArteryTlsSetup`, or set `WithArteryRemoting`'s `options.Tls`. |
| `certificate.path`, `password`, `flags` | `TlsCertificateLoader.LoadPkcs12FromFile(path, password, keyStorageFlags)`. |
| `certificate.use-thumbprint-over-file`, `thumbprint`, `store-name`, `store-location` | `TlsCertificateLoader.LoadFromStore(thumbprint, storeName, storeLocation, validOnly)`. Choose `validOnly` explicitly; it defaults to `true`. |
| `DotNettySslSetup.Certificate` / `Ssl.X509Certificate` | Pass the existing caller-owned `X509Certificate2` to the TLS factory. |
| `require-mutual-authentication = on` / `off` | `ArteryTlsSettings.Mutual(...)` / `ServerOnly(...)`. |
| `suppress-validation = off` | `TlsPeerPolicy.SystemTrust()` for operating-system chain trust. |
| `suppress-validation = on` | Choose an explicit trust policy, such as `PinnedCertificates(...)` for known self-signed peers, or `CustomTrust(...)` for your own acceptance rules. |
| `validate-certificate-hostname = on` | Add `.And(TlsCertificateValidation.ValidateHostname())` to the outbound server policy. |
| `CustomValidator` and `CertificateValidation` helpers | `TlsPeerPolicy.CustomTrust(...)` and the corresponding `TlsCertificateValidation` helpers. The callback has the same five arguments; wrap an existing delegate in a lambda to adapt its type. |

`SystemTrust()` alone leaves hostname checking off. To enable it, use
`ArteryTlsSettings.Mutual(certificate, inboundClientPolicy, outboundServerPolicy)` and add
`ValidateHostname()` to the outbound policy. This validates the remote endpoint's host;
inbound clients do not have an independently known DNS identity to infer from their address.
Use explicit pins, subject/issuer checks, or a custom policy for inbound identity rules.

`CustomTrust(...)` replaces the complete trust decision; `.And(...)` only adds restrictions.
Pins alone do not add chain, expiry, revocation, or hostname checks. A missing required
certificate is rejected even if a callback would accept it. Store lookup's `validOnly` filter
is separate from peer validation; DotNetty tied that filter to `suppress-validation`.

As with any classic-to-Artery migration, switch every participating node to the same
transport and update actor and seed-node addresses. A TLS-enabled classic address such as
`akka.ssl.tcp://MySystem@host:2552` becomes `akka://MySystem@host:25520` with Artery TLS
configured in code. Classic and Artery cannot communicate during a mixed-transport rollout.

See [TLS for Akka.IO, Akka.Streams, and Artery](docs/articles/remoting/tls-for-io-streams-artery.md)
for certificate-store loading, pin rotation, custom policies, Streams APIs, and further
compiled examples. The console example above is also maintained in
[ArteryTlsExamples.cs](src/core/Akka.Docs.Tests/Networking/ArteryTlsExamples.cs).

## Getting Help and Reporting Beta Issues

Found a bug in the v1.6 beta, or something in this guide doesn't match what you see?
[Open a GitHub issue](https://github.com/akkadotnet/akka.net/issues). For questions, use
[GitHub Discussions](https://github.com/akkadotnet/akka.net/discussions) or
[Akka.NET on Discord](https://discord.gg/GSCfPwhbWP).
