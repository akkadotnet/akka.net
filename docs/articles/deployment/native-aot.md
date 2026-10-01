---
uid: native-aot
title: Native AOT and Trimming
---
# Native AOT and Trimming

Akka.NET core can boot a local `ActorSystem` under .NET Native AOT. So can an Akka.Hosting
application, with dependency injection, a custom extension, and health checks. This page describes
what that covers, how to turn it on, and what is still missing. Progress is tracked in
[#7246](https://github.com/akkadotnet/akka.net/issues/7246).

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
* Three `Setup` types that take an instance or factory instead of a HOCON type name: `LoggerSetup`
  (custom loggers and log formatter), `SerializationSetup` (custom serializers and bindings), and
  `ExtensionsSetup` (custom or third-party extensions). Combine them (plus a `BootstrapSetup` if you
  need one) with `ActorSystemSetup` and pass the result to `ActorSystem.Create`.
* Serializer names for Akka.Remote, Akka.Cluster, Akka.Cluster.Tools, Akka.Cluster.Sharding,
  Akka.Cluster.Metrics, Akka.DistributedData, Akka.Persistence and Akka.Streams: each resolves its
  own `reference.conf` serializer rows from a built-in table instead of `Type.GetType`, the same as
  core's.
* A first-party table for `akka.extensions`: `DistributedDataProvider`,
  `DistributedPubSubExtensionProvider`, `ClusterClientReceptionistExtensionProvider` and
  `ClusterMetricsExtensionProvider` resolve by name when their assembly is present. Anything else -
  your own extension, or any other third-party one - needs `ExtensionsSetup`.

`src/aot/Akka.AOT.App` and `src/aot/Akka.Hosting.AOT.App` in the repository are the reference
applications, for core and for Akka.Hosting. Both publish under Native AOT, fail the run if anything
logs a `Warning` or `Error` during startup, and run in CI on every pull request, each checked against
its own warning baseline (`src/aot/*/aot-warnings.baseline.txt`).

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

**Reading the warnings.** Add `<TrimmerSingleWarn>false</TrimmerSingleWarn>` to see every call site
instead of one per assembly. A local-only app that references just core should get none of them from
Akka; one that shows up from Akka.Remote, Akka.Cluster or Akka.Persistence marks a call site likely
to fail at runtime - see [Not Supported Yet](#not-supported-yet).

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

If your own serializer writes a manifest - a `SerializerWithStringManifest`, or a plain `Serializer`
that sets `IncludeManifest` - map that manifest back to a `Type` yourself: override
`FromBinary(byte[], string)`, or derive from `SerializerWithStringManifest` and implement it there.
With the switch off, the base `Serializer.FromBinary(byte[], string)` throws a `SerializationException`
for a manifest it has not already cached (#8697). Persistence's `PersistentFSM.PersistentFSMSnapshot<>`
stays on that base fallback today, because it is generic.

## Not Supported Yet

* **Akka.Remote with classic DotNetty remoting.** Does not work under Native AOT: transport,
  failure-detector and adapter classes still load from HOCON by `Type.GetType`, so startup fails with
  `Cannot instantiate transport [...TcpTransport,Akka.Remote]`. Artery (`akka.remote.artery.enabled =
  on`, experimental) is the target transport for AOT; no CI canary covers it yet.
* **Akka.Cluster, Akka.Persistence and the cluster tools.** Their serializers resolve from built-in
  tables, but other HOCON type names they read still go through `Type.GetType` with no switch guard
  (`downing-provider-class`, journal/snapshot plugin `class`, event adapters, the DData durable
  store, a custom sharding state store). Treat them as unsupported under Native AOT for now.
* **Remote deployment.** Akka.Remote's `DaemonMsgCreateSerializer` resolves a remotely deployed
  actor's implementation type from the wire with `Type.GetType(protoProps.Clazz)` in
  `PropsFromProto` - no built-in table, no switch guard. `Props.TypeName` and the `Props` surrogate
  are a related but separate path: what a serializer such as the default JSON one uses whenever it
  has to carry a `Props` value directly.
* **Custom mailboxes and dispatchers.** With the switch off these can only be the built-in ones.
  Unlike loggers and extensions - see [What Works Today](#what-works-today) - there is no `Setup` API
  yet for supplying a custom mailbox or dispatcher as a factory delegate instead of a type name.
* **Stream refs with the switch off.** `SerializationTools.TypeFromString` has no built-in table for
  a stream ref's element type - it is generic over any type the application chooses - so it throws:
  "stream refs need Akka.DynamicTypeLoading enabled at publish time" (#8667).
* **The default JSON serializer.** Core does not register `json` or its `System.Object` binding when
  the switch is off, so message types need serializers you register yourself. Newtonsoft.Json is
  then trimmed away as a result - that is the consequence, not the cause.
  [Source-generated MessagePack serialization](xref:source-generated-serialization) is built for
  trimming and Native AOT instead - no reflection, registration through a generated
  `CreateRegistration()`/`CreateSetup()` - though no AOT canary covers it yet.
