# Akka.NET v1.6 Breaking Changes

This file lists the breaking changes on the `dev` branch during the Akka.NET **v1.6**
development cycle, ahead of a stable `v1.6.0` release.

## What counts as breaking

Compare against the **last stable v1.5 release** (`1.5.71` at the time of writing). A change
is breaking if it is not:

- **Binary compatible**: an assembly compiled against v1.5 fails to load or bind (removed or
  renamed public members, changed signatures, changed types).
- **Source compatible**: existing code stops compiling, or gets new errors or
  warnings-as-errors the user can't avoid. New trim-analyzer warnings count only for apps
  that run the trim analyzer; list them briefly.
- **Behaviorally compatible**: an existing app with its existing config sees different
  runtime behavior: different results, exceptions where it used to work, different wire
  output, different defaults, or a removed setting that used to have an effect.

Not breaking, so not listed: behavior that only exists with `Akka.DynamicTypeLoading` off (the
switch is new in v1.6 and on by default; #8608 is listed because it turns the switch off for
you); fixes that make something work that used to fail, unless users plausibly relied on the
failure; differences from `dev` states that never shipped; internal refactors; new APIs.

## Process and lifecycle

- A PR that makes a breaking change says so in a `## Breaking changes` section of its
  description. Maintainers batch those into this file.
- This file is kept **only until a stable `v1.6.0` ships**. Its contents then move into the
  release notes / upgrade guide and the file is retired.

## Entry format

Newest first, one row per change. Keep `Change` to a line or two and `Migration` to one; link
the PR for detail. `Type` is one or more of `Binary`, `Source`, `Behavior` (combine with `+`);
add `Wire` when the bytes on the wire change.

| Status | PR | Component | Type | Change | Migration |
|--------|----|-----------|------|--------|-----------|
| Merged | link | `Akka.Xyz` | Behavior | one-line summary | what users must do |

---

## Changes

| Status | PR | Component | Type | Change | Migration |
|--------|----|-----------|------|--------|-----------|
| Merged | [#8698](https://github.com/akkadotnet/akka.net/pull/8698) | `Akka` (serialization) | Behavior + Wire | A user `serializers` row that names a built-in serializer type under a second alias now gets the shared built-in instance, and that alias's own `serialization-settings` block is ignored. For `PrimitiveSerializers` this can change the manifests it writes. | Put the settings on the built-in alias's block (e.g. `serialization-settings.primitive`) instead of a second alias. |
| Merged | [#8697](https://github.com/akkadotnet/akka.net/pull/8697), [#8655](https://github.com/akkadotnet/akka.net/pull/8655), [#8649](https://github.com/akkadotnet/akka.net/pull/8649), [#8603](https://github.com/akkadotnet/akka.net/pull/8603), [#8602](https://github.com/akkadotnet/akka.net/pull/8602) | `Akka`, `Akka.DependencyInjection`, `Akka.Hosting` | Source (trim analyzer only) | New trimming attributes on public APIs (`Props` and `ActorOf<T>`/`SystemActorOf<T>`, `IIndirectActorProducer.ActorType`, `IDependencyResolver.Props`, `MailboxType`/`Mailboxes`, `TypeCache.GetType`, Hosting's `WithExtension<T>`/`WithExtensions`). Apps that run the trim analyzer may get new `IL2026`/`IL2067`/`IL2072`/`IL2091` warnings at call sites, and `IL2092`-`IL2095` on implementations and overrides. | Annotate the `Type` or type parameter the value flows from (or pass `typeof(...)`); implementations repeat the attribute. |
| Merged | [#8694](https://github.com/akkadotnet/akka.net/pull/8694) | `Akka` and modules (serialization) | Behavior + Wire | Built-in serializers now declare their ids in code, so an `akka.actor.serialization-identifiers` override for a built-in serializer type no longer has any effect. Subclasses still read their id from HOCON. | Remove such overrides. A cluster where every node carried the same override must upgrade all nodes together. |
| Merged | [#8695](https://github.com/akkadotnet/akka.net/pull/8695), [#8669](https://github.com/akkadotnet/akka.net/pull/8669), [#8666](https://github.com/akkadotnet/akka.net/pull/8666), [#8658](https://github.com/akkadotnet/akka.net/pull/8658), [#8605](https://github.com/akkadotnet/akka.net/pull/8605), [#8604](https://github.com/akkadotnet/akka.net/pull/8604), [#8602](https://github.com/akkadotnet/akka.net/pull/8602), [#8601](https://github.com/akkadotnet/akka.net/pull/8601) | `Akka`, `Akka.Remote`, `Akka.Streams`, `Akka.Cluster`, cluster contrib, `Akka.Persistence` | Behavior | Akka now constructs its own scheduler, mailboxes and serializers directly instead of through `Activator.CreateInstance`, and `Props.Create(() => new A(...))` evaluates arguments without `DynamicInvoke`. An exception thrown there is no longer wrapped in `TargetInvocationException`, and a rejected built-in mailbox's error names the configured type string. | Catch the inner exception type. |
| Merged | [#8649](https://github.com/akkadotnet/akka.net/pull/8649) | `Akka.Hosting` | Behavior | `WithExtension<T>()`/`WithExtensions(...)` pass extensions through `ExtensionsSetup`, so `akka.extensions` no longer lists them. An extension id whose constructor throws now fails startup with `ConfigurationException`; v1.5 logged and skipped it. | Use `Settings.Setup.Get<ExtensionsSetup>()` or `HasExtension<T>()` instead of reading `akka.extensions`. |
| Merged | [#8660](https://github.com/akkadotnet/akka.net/pull/8660) | `Akka.IO` | Binary + Source | Removed `Tcp.ResumeWriting`, `Tcp.WritingResumed`, `Tcp.Register.UseResumeWriting`, `TcpMessage.ResumeWriting()` and the `useResumeWriting` parameter of `Tcp.Register`/`TcpMessage.Register`. They never did anything. | Drop the `useResumeWriting` argument and any `ResumeWriting` sends; use ack-based writes for backpressure. |
| Merged | [#8646](https://github.com/akkadotnet/akka.net/pull/8646) (TCP rewrite in [#8132](https://github.com/akkadotnet/akka.net/pull/8132)) | `Akka.IO` | Behavior | `akka.io.tcp.write-commands-queue-max-size` now caps a single write, not the total bytes queued on a connection, so writes that push the backlog past it are no longer rejected with `CommandFailed`. Backpressure comes from `WriteAck`, which waits until the output buffer drains below its resume mark. | Use ack-based writes if you relied on the setting to bound memory. |
| Merged | [#8608](https://github.com/akkadotnet/akka.net/pull/8608) | `Akka` (package build logic) | Behavior | Projects that set `PublishAot` or `PublishTrimmed` now get `Akka.DynamicTypeLoading` turned off automatically. There, a HOCON type name outside Akka's built-in tables (custom logger, mailbox, dispatcher, router, serializer, scheduler, extension, provider, DNS provider) throws `ConfigurationException` instead of loading by reflection, and receiving a stream ref fails. Untrimmed builds are unaffected. | Register custom types through `Setup` APIs (`SerializationSetup`, `ExtensionsSetup`, `LoggerSetup`, ...), or opt back in with `<RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="true" />`. |
| Merged | [#8606](https://github.com/akkadotnet/akka.net/pull/8606) | `Akka.IO` | Behavior | UDP no longer pools buffers. The `akka.io.udp(-connected).buffer-pool` and `direct-buffer-pool` settings and the pool sections' `class` keys are gone; UDP always reads `disabled-buffer-pool.buffer-size`. | If you pointed `buffer-pool` at `direct-buffer-pool` or a custom pool, move its `buffer-size` to `disabled-buffer-pool.buffer-size`, or datagrams over 512 bytes are truncated. |
| Merged | [#8599](https://github.com/akkadotnet/akka.net/pull/8599), [#8670](https://github.com/akkadotnet/akka.net/pull/8670) | `Akka` (`Settings`) | Behavior | `Settings.ProviderSelectionType`/`ProviderClass` now report the built-in provider when `akka.actor.provider` names one by type, including the default config: v1.5 gave `Custom`/`"Akka.Actor.LocalActorRefProvider"`, v1.6 gives `Local`/`"Akka.Actor.LocalActorRefProvider, Akka"`. | Test `ProviderSelectionType is ProviderSelection.Local` instead of comparing `ProviderClass` strings. |
| Merged | [#8594](https://github.com/akkadotnet/akka.net/pull/8594) | `Akka.Hosting` family, `Akka`, `Akka.DependencyInjection` | Binary + Source | The Akka.Hosting packages now ship from this repository at the Akka.NET version and target `net10.0` only (Akka.Hosting 1.5: `netstandard2.0;net6.0`). The `Microsoft.Extensions.*` minimum moves to 10.0.0 for `Akka`, `Akka.DependencyInjection` and the Hosting packages. | Target `net10.0` or later and reference `Akka.Hosting.*` at the same version as `Akka.*`. |
| Merged | [#8545](https://github.com/akkadotnet/akka.net/pull/8545) | `Akka.TestKit.Xunit` | Source | `TestKit` now implements `IAsyncLifetime` with `virtual` `InitializeAsync()`/`DisposeAsync()`, so a derived spec that declares its own gets `CS0114` (an error under warnings-as-errors). | Mark them `override` and call `base.InitializeAsync()`/`base.DisposeAsync()`. |
| Merged | [#8445](https://github.com/akkadotnet/akka.net/pull/8445) | `Akka.Cluster.Sharding` | Behavior | The default `least-shard-allocation-strategy.rebalance-absolute-limit` is now `20` (v1.5: `0`), which selects the bounded rebalancing strategy; `rebalance-threshold` and `max-simultaneous-rebalance` no longer apply by default. | Set `rebalance-absolute-limit = 0` to keep the legacy strategy for now. |
| Merged | [#8378](https://github.com/akkadotnet/akka.net/pull/8378) | `Akka.Remote.TestKit` | Behavior | The multi-node `Player` now fails a throttle or blackhole request that the transport did not apply; v1.5 reported success for the no-op. | Set `TestTransport = true` in the `MultiNodeConfig` of specs that throttle or blackhole. |
| Merged | [#8359](https://github.com/akkadotnet/akka.net/pull/8359) | `Akka.Cluster` | Behavior | `Cluster.Get()` no longer blocks until the cluster core has started, and no longer fails with "Failed to startup Cluster" after `akka.actor.creation-timeout`; core startup finishes in the background. | Don't treat `Cluster.Get()` returning as "cluster started"; use `JoinAsync`, `RegisterOnMemberUp` or cluster events. |
| Merged | [#8317](https://github.com/akkadotnet/akka.net/pull/8317) | `Akka.Remote`, `Akka.Cluster` | Binary + Source | System UIDs are now `long` instead of `int`: `AddressUid.Uid`, `AddressUidExtension.Uid()`, `UniqueAddress`'s constructor and `Uid`, `QuarantinedEvent`, `RemoteWatcher.HeartbeatRsp`, and every `Quarantine(Address, int?)` member. The wire format is unchanged while UIDs stay in 32-bit range (the default). | Recompile and use `long` for UIDs. Turn on `akka.remote.use-64bit-system-uids` only once every node runs v1.6. |
| Merged | [#8263](https://github.com/akkadotnet/akka.net/pull/8263) | `Akka.Streams` | Behavior | The actor materialized by `Source.ActorRef<T>` now ignores `PoisonPill` and `Kill` instead of completing the stream. | Send `Status.Success` to complete the stream or `Status.Failure` to fail it. |

### Not breaking, for the record

- **Artery** is new in v1.6 (v1.5.71 has no Artery), so changes to its behavior and settings
  are not listed.
- **#8317 wire**: cluster gossip's `UniqueAddress.uid` went from `uint32` to `uint64`. Both use
  the same varint encoding, so v1.5 and v1.6 nodes interoperate while UIDs stay 32-bit.
- **#8484 gossip tombstones**: gossip gains an additive `tombstones` field that v1.5 nodes
  ignore, plus a new `akka.cluster.prune-gossip-tombstones-after` setting (default `24h`).
