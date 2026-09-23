---
uid: native-aot
title: Native AOT and Trimming
---
# Native AOT and Trimming

Akka.NET core can boot a local `ActorSystem` under .NET Native AOT and under a trimmed publish. This
page describes what that covers, how to turn it on, and what is still missing. Progress is tracked in
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

`src/aot/Akka.AOT.App` in the repository is the reference application. It publishes under Native AOT,
asserts that nothing warns during startup, and runs in CI on every pull request.

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
and an unserializable message type throws `SerializationException` when you send it rather than at
startup.

Serialization is the case worth planning for. With the switch off core does not register the
reflection-driven `json` serializer, nor the `System.Object` binding that points at it, so a message
type with no `serialization-bindings` entry of its own has no fallback and throws when serialized.
Register a serializer for your own message types through a `SerializationSetup`. This is a
consequence of the switch, not of trimming - the same thing happens on the JIT if you turn the
switch off there.

## Not Supported Yet

* **Akka.Remote and Akka.Cluster.** Classic DotNetty remoting does not work under Native AOT. The
  Artery transport is the target for remote and cluster AOT support; until it lands, treat anything
  beyond a local `ActorSystem` as unsupported.
* **Custom mailboxes, loggers, dispatchers and extensions.** With the switch off these can only be
  the built-in ones. The `Setup` APIs that will let you supply your own as a factory delegate,
  instead of a type name for core to resolve, are still in progress.
* **`akka.extensions`.** Extensions are still loaded by assembly-qualified name and are not covered
  by a table.
* **The default JSON serializer.** Core does not register `json` or its `System.Object` binding when
  the switch is off, so message types need serializers you register yourself. Newtonsoft.Json is
  then trimmed away as a result - that is the consequence, not the cause.
