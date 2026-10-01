---
uid: native-aot
title: Native AOT and Trimming
---
# Native AOT and Trimming

Akka.NET core can boot a local `ActorSystem` under .NET Native AOT and under a trimmed publish. So
can an Akka.Hosting application, with dependency injection, a custom extension, and health checks.
This page describes what that covers, how to turn it on, and what is still missing. Progress is
tracked in [#7246](https://github.com/akkadotnet/akka.net/issues/7246).

The mechanism is one feature switch, `Akka.DynamicTypeLoading`. It is **on** by default, so nothing
about an ordinary build changes. When it is off, every place core would have resolved a HOCON type
name with `Type.GetType` instead reads a built-in table, and the trimmer removes the reflection
fallback entirely.

## What Works Today

* A local `ActorSystem`, from the default config or through a `BootstrapSetup`.
* `UntypedActor`, `ReceiveActor`, `Props`, `Ask`, the scheduler, `EventStream` and coordinated
  shutdown.
* Every type name core's own `akka.conf` ships: the loggers under `akka.loggers`, the stdout logger
  and log formatter, the scheduler, the five built-in mailbox types and their message-queue
  semantics, the `bytes` serializer and its binding, the built-in routers under
  `akka.actor.router.type-mapping`, the guardian supervisor strategy, and the dispatcher and executor
  types.
* `akka.actor.provider = local`, plus the `remote` and `cluster` selections as far as core is
  concerned - see [Not Supported Yet](#not-supported-yet) for what those two actually need.
* An Akka.Hosting application built with `AddAkka`: dependency injection through
  Akka.DependencyInjection, a custom extension registered with `WithExtension`, log output routed
  through `Microsoft.Extensions.Logging`, and the built-in `ActorSystem` liveness health check.
* Four `Setup` types that take an instance or a factory delegate instead of a HOCON type name:
  `BootstrapSetup`, `LoggerSetup` (custom loggers and log formatter), `SerializationSetup` (custom
  serializers and bindings), and `ExtensionsSetup` (custom or third-party extensions). Combine the
  ones you need with `ActorSystemSetup` and pass the result to `ActorSystem.Create`.
* Serializer names for every module Akka.NET ships, not only core: Akka.Remote, Akka.Cluster,
  Akka.Cluster.Tools, Akka.Cluster.Sharding, Akka.Cluster.Metrics, Akka.DistributedData,
  Akka.Persistence and Akka.Streams each resolve their own `reference.conf` serializer rows from a
  built-in table instead of `Type.GetType`. This keeps serialization setup trim-safe; it does not by
  itself mean the module boots clean end to end under Native AOT - see
  [Not Supported Yet](#not-supported-yet).
* A first-party table for `akka.extensions`: `DistributedData`, `DistributedPubSub`,
  `ClusterClientReceptionist` and `ClusterMetrics` resolve by name when their assembly is present.
  Anything else - your own extension, or any other third-party one - needs `ExtensionsSetup`.

`src/aot/Akka.AOT.App` and `src/aot/Akka.Hosting.AOT.App` in the repository are the reference
applications, for core and for Akka.Hosting respectively. Both publish under Native AOT, assert that
nothing warns during startup, and run in CI on every pull request, each checked against its own
warning baseline (`src/aot/*/aot-warnings.baseline.txt`).

## Turning It On

If you reference the `Akka` NuGet package, nothing to do. The package ships an MSBuild `.targets`
file that turns the switch off whenever your project publishes with Native AOT or trimming:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

`PublishTrimmed` works the same way. To keep reflection alive - because you have rooted a type by
hand and want `Type.GetType` to keep finding it - declare the option yourself with `true`, and your
value wins over the package's:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="true" />
</ItemGroup>
```

Turning the switch back on this way keeps every reflection fallback reachable, so the
`IL2026`/`IL3050` trim warnings at those call sites come back too, and you take on rooting whatever
type you need HOCON to resolve.

Setting it to `false` the same way turns the trimming-safe paths on for an ordinary, untrimmed build,
which is a useful way to test them. Either value may be declared anywhere MSBuild reads - the
project file, `Directory.Build.props`, `Directory.Build.targets`, a publish profile or `-p` on the
command line - because the package applies its default from inside a target, after every file has
been evaluated.

## What Changes With the Switch Off

Only the names in the built-in tables resolve. Nearly anything else throws a `ConfigurationException`
naming the HOCON setting, the value it could not resolve, and the switch, so a misconfiguration fails
loudly at startup instead of producing a half-built actor system. Two settings behave differently:
`akka.io.dns.inet-address.provider-object` logs a warning and carries on with the built-in provider,
and a message type with no serializer throws `SerializationException` when you send it rather than at
startup.

Serialization is the case worth planning for. With the switch off core does not register the
reflection-driven `json` serializer, nor the `System.Object` binding that points at it, so a message
type with no `serialization-bindings` entry of its own has no fallback and throws when serialized.
Register a serializer for your own message types through a `SerializationSetup`. This is a
consequence of the switch, not of trimming - the same thing happens on the JIT if you turn the
switch off there.

## Not Supported Yet

* **Akka.Remote, Akka.Cluster, Akka.Persistence, and the cluster tools** (Cluster.Sharding,
  DistributedPubSub, ClusterSingleton, DistributedData, and friends). Each module's own serializers
  resolve from a built-in table with the switch off, the same as core's - see
  [What Works Today](#what-works-today). But no canary boots an `ActorSystem` through any of these
  modules under Native AOT, so this combination is **not yet verified**, not unsupported. Artery, the
  planned remote transport, is the long-term target here; see
  [#7246](https://github.com/akkadotnet/akka.net/issues/7246).
* **Remote deployment.** `Props.TypeName` and the `Props` surrogate still resolve a deployed actor's
  implementation type by name for remote deployment. That lookup stays reflection-based, so it is not
  trim-safe yet.
* **Custom mailboxes and dispatchers.** With the switch off these can only be the built-in ones.
  Unlike loggers and extensions - see [What Works Today](#what-works-today) - there is no `Setup` API
  yet for supplying a custom mailbox or dispatcher as a factory delegate instead of a type name.
* **The default JSON serializer.** Core does not register `json` or its `System.Object` binding when
  the switch is off, so message types need serializers you register yourself. Newtonsoft.Json is
  then trimmed away as a result - that is the consequence, not the cause. For a trim- and AOT-safe
  way to serialize your own message types, reach for
  [source-generated MessagePack serialization](xref:source-generated-serialization) instead of
  writing a classic `Serializer` by hand.
