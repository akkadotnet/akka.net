#### 1.6.0-beta1 October 2nd, 2026 ####

Akka.NET 1.6.0-beta1 is the first public beta of the 1.6 line. Every package now targets .NET 10 only. This beta adds an experimental Artery TCP remoting transport, a buffer-based `SerializerV2` API with a source generator for your own messages, a rewritten Akka.IO TCP layer, Native AOT support for local and Akka.Hosting apps, and code-based serializer registration for Akka's built-in modules. Akka.Hosting now ships from this repository. This release has breaking changes.

Start here: [Akka.NET v1.6 getting started guide](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta1/GETTING_STARTED_V1.6.md)

Upgrading from 1.5? Read the [breaking changes](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta1/BREAKING_CHANGES_V1.6.md).

**Breaking changes**

The ledger linked above lists every change with its migration step. The ones most apps will hit:

* **.NET 10 only.** Every package, Akka.Hosting included, targets `net10.0` only, so .NET Framework, .NET 6 and .NET 8 apps can't reference 1.6. Dependency minimums rise to `Microsoft.Extensions.*` 10.0 for `Akka`, `Akka.DependencyInjection` and Akka.Hosting (Akka.Hosting 1.5.71 needed 9.0), Google.Protobuf 3.36.1 ([#8577](https://github.com/akkadotnet/akka.net/pull/8577)), LightningDB 0.22.0 for Akka.DistributedData.LightningDB ([#8338](https://github.com/akkadotnet/akka.net/pull/8338)) and FSharp.Core 10.1.302 for Akka.FSharp and Akka.Persistence.FSharp ([#8386](https://github.com/akkadotnet/akka.net/pull/8386)). Move your app to .NET 10. ([#8132](https://github.com/akkadotnet/akka.net/pull/8132), [#8594](https://github.com/akkadotnet/akka.net/pull/8594))
* **`Akka.IO.ByteString` is gone.** TCP (`Tcp.Received`, `Tcp.Write`) and the Streams byte APIs (`Framing`, `JsonFraming`, `FileIO`, `StreamConverters`, `Tcp`) use `ReadOnlySequence<byte>`, UDP uses `ReadOnlyMemory<byte>`, and `ByteOrder` moved to `Akka.Util`. Replace `ByteString` with `byte[]`, `ReadOnlyMemory<byte>` or `ReadOnlySequence<byte>`. ([#8132](https://github.com/akkadotnet/akka.net/pull/8132))
* **No more HOCON from `App.config` / `Web.config`.** `ConfigurationFactory.Load()`, `AkkaConfigurationSection` and Akka.FSharp's `Configuration.load` are removed. Load HOCON from a string or file with `ConfigurationFactory.ParseString`, or configure through Akka.Hosting. ([#7456](https://github.com/akkadotnet/akka.net/pull/7456))
* **Akka.Hosting** ships in lockstep with Akka.NET, so reference every `Akka.Hosting.*` package at the same version as `Akka.*`. `WithExtension<T>()` now registers through `ExtensionsSetup`; see **Akka.Hosting now ships from this repository** below. ([#8594](https://github.com/akkadotnet/akka.net/pull/8594), [#8649](https://github.com/akkadotnet/akka.net/pull/8649))
* **Serialization.** An `akka.actor.serialization-identifiers` override for a built-in serializer no longer has any effect ([#8694](https://github.com/akkadotnet/akka.net/pull/8694)). A second alias that names a built-in Akka.Remote/Akka.Cluster/... serializer type gets the shared built-in instance, and its own `serialization-settings` block is ignored ([#8698](https://github.com/akkadotnet/akka.net/pull/8698)). `Serializer` gains virtual `FromBinary(byte[], string)` and `Manifest(object)`, so `FromBinary(bytes, null)` no longer compiles without a cast, and `ByteArraySerializer.IncludeManifest` is now `true` ([#8222](https://github.com/akkadotnet/akka.net/pull/8222)). Module serializers now handle some types that used to fall back to `json`; see **Serialization registration** below ([#8702](https://github.com/akkadotnet/akka.net/pull/8702)).
* **Akka.Cluster.Sharding** uses bounded rebalancing by default (`least-shard-allocation-strategy.rebalance-absolute-limit = 20`), which changes how a running cluster moves shards. Set it to `0` to keep the old strategy. ([#8445](https://github.com/akkadotnet/akka.net/pull/8445))

Also breaking - see the ledger: `long` system UIDs ([#8317](https://github.com/akkadotnet/akka.net/pull/8317)); Akka.IO TCP writes, UDP buffers and DNS ([#8132](https://github.com/akkadotnet/akka.net/pull/8132), [#8606](https://github.com/akkadotnet/akka.net/pull/8606), [#8646](https://github.com/akkadotnet/akka.net/pull/8646), [#8660](https://github.com/akkadotnet/akka.net/pull/8660)); `Cluster.Get()` no longer blocks ([#8359](https://github.com/akkadotnet/akka.net/pull/8359)); `Source.ActorRef<T>` ignores `PoisonPill`/`Kill` ([#8263](https://github.com/akkadotnet/akka.net/pull/8263)); DotNetty `ValidateHostname` checks the name ([#8465](https://github.com/akkadotnet/akka.net/pull/8465)); `Akka.DynamicTypeLoading` turns off for AOT/trimmed publishes ([#8608](https://github.com/akkadotnet/akka.net/pull/8608)); TestKit `IAsyncLifetime` ([#8545](https://github.com/akkadotnet/akka.net/pull/8545)); no more `TargetInvocationException` wrapping ([#8601](https://github.com/akkadotnet/akka.net/pull/8601) and others); `ProviderSelectionType`/`ProviderClass` ([#8599](https://github.com/akkadotnet/akka.net/pull/8599)); the multi-node `Player` needs `TestTransport = true` to throttle or blackhole ([#8378](https://github.com/akkadotnet/akka.net/pull/8378)); new trimming attributes on public APIs.

**Native AOT and trimming**

* CI publishes two apps with `PublishAot=true` and runs them on every pull request: a local `ActorSystem` (`src/aot/Akka.AOT.App`) and an Akka.Hosting app with dependency injection, a custom extension, `Microsoft.Extensions.Logging` and health checks (`src/aot/Akka.Hosting.AOT.App`). Both apps publish with no trim or AOT warnings from the Akka code they use. CI fails on any new warning in core (checked against a full, rooted analysis of `Akka.dll`) or in Akka.Hosting, Akka.DependencyInjection and Akka.Streams.
* A new feature switch, `Akka.DynamicTypeLoading`, controls whether Akka may resolve HOCON type names with `Type.GetType`. It stays on for ordinary builds. The `Akka` package turns it off for you when your project sets `PublishAot` or `PublishTrimmed`; with it off, only Akka's built-in type names resolve and nearly anything else throws a `ConfigurationException` that names the setting. To turn it back on, add `<RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="true" />`. ([#8601](https://github.com/akkadotnet/akka.net/pull/8601), [#8608](https://github.com/akkadotnet/akka.net/pull/8608))
* Two new `Setup` types, `LoggerSetup` (custom loggers and the log formatter, [#8218](https://github.com/akkadotnet/akka.net/pull/8218)) and `ExtensionsSetup` (extensions, [#8648](https://github.com/akkadotnet/akka.net/pull/8648)), join the existing `SerializationSetup` (serializers and bindings), so you can register your own types without HOCON type names. Combine them with `ActorSystemSetup` and pass the result to `ActorSystem.Create`. Akka.Hosting's `WithExtension<T>()` uses `ExtensionsSetup` ([#8649](https://github.com/akkadotnet/akka.net/pull/8649)).
* With the switch off, core doesn't register the reflection-based `json` serializer or its `System.Object` binding, so register a serializer for your own messages through `SerializationSetup`; the source-generated serializer in `Akka.Serialization.V2` is built for this. Stream refs need the switch left on.
* Not supported under Native AOT yet: classic DotNetty remoting (fails at startup, because it loads its transport by type name); Akka.Cluster (including Sharding and DData), Akka.Persistence and the cluster tools (ClusterClient, PubSub, Singleton); remote deployment; custom mailboxes and dispatchers. Artery is the target transport for Akka.Remote under AOT, but no CI check covers Artery under AOT yet.
* Full details: [Native AOT and Trimming](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta1/docs/articles/deployment/native-aot.md). Progress is tracked in [#7246](https://github.com/akkadotnet/akka.net/issues/7246).

**Serialization registration**

* Built-in serializers declare their wire ids in code instead of reading them from `akka.actor.serialization-identifiers`. The ids don't change. A subclass of a built-in serializer still reads its id from HOCON. ([#8694](https://github.com/akkadotnet/akka.net/pull/8694))
* Akka's built-in modules (Remote, Streams, Cluster, Cluster.Tools, Cluster.Sharding, DistributedData, Cluster.Metrics, Persistence) each carry a C# table of their serializers, aliases and bindings, so their serializers resolve without `Type.GetType` ([#8645](https://github.com/akkadotnet/akka.net/pull/8645), [#8658](https://github.com/akkadotnet/akka.net/pull/8658), [#8666](https://github.com/akkadotnet/akka.net/pull/8666), [#8669](https://github.com/akkadotnet/akka.net/pull/8669), [#8695](https://github.com/akkadotnet/akka.net/pull/8695), [#8698](https://github.com/akkadotnet/akka.net/pull/8698)). The modules' HOCON serializer rows are still there.
* Every built-in module that ships with your app registers its serializers and bindings from that table as defaults when the `ActorSystem` starts, whether or not you configure or start that module. Precedence is module defaults, then HOCON, then `SerializationSetup`, each overriding the one before. A module message that arrives before its extension starts now deserializes instead of being dropped, and stream refs work before any materializer exists. Replacing a module default no longer logs the override warning. ([#8702](https://github.com/akkadotnet/akka.net/pull/8702))
* What that means for local apps: on any system that ships `Akka.Remote.dll` or `Akka.Cluster.dll`, even one that never turns on remoting or clustering, these types no longer fall back to `json` (or your own `System.Object` binding):
  * `string`, `int`, `long` go to `primitive` (id 17)
  * `Google.Protobuf.IMessage` goes to `proto` (id 2)
  * `Akka.dll` types such as `IActorRef`, `PoisonPill`, `Kill`, `Identify` / `ActorIdentity`, `Status.Success` / `Status.Failure`, the router pools and `Config` go to `akka-misc` (id 16); `ActorSelectionMessage` to `akka-containers` (id 6); `SystemMessage` to `akka-system-msg` (id 22)
  * `IDeliverySerializable` (reliable delivery, in Akka.dll) goes to `reliable-delivery` (id 36) when `Akka.Cluster.dll` ships

  Akka.Persistence writes events and snapshots of these types with the module serializer. 1.6 still reads your old rows, but a rollback to 1.5.x can't read the new ones unless that process also loads Akka.Remote's config (Akka.Cluster's for `reliable-delivery`). A type that implements one of these interfaces and one of your own bound interfaces now matches two bindings. To keep the old serializer for a type, or to settle an ambiguous match, bind the concrete type in `akka.actor.serialization-bindings` or through `SerializationSetup`.
* Serializers resolve their own manifests. `SystemMessageSerializer` and the two Persistence serializers map manifests to types from their own tables, and `SerializerV1Adapter` now calls a plain `Serializer`'s own `FromBinary(byte[], string)` override instead of skipping it. With `Akka.DynamicTypeLoading` off, the base `Serializer.FromBinary(byte[], string)` throws for a manifest it hasn't seen, so a custom serializer that writes a manifest should override that method or derive from `SerializerWithStringManifest`. ([#8697](https://github.com/akkadotnet/akka.net/pull/8697))

**SerializerV2 and source-generated serializers**

* `SerializerV2` is a new public base class that writes to an `IBufferWriter<byte>` and reads from a `ReadOnlySequence<byte>`, instead of returning a new `byte[]` per call. ([#8222](https://github.com/akkadotnet/akka.net/pull/8222))
* `Serialization` stores every serializer as a `SerializerV2`. Existing `Serializer` and `SerializerWithStringManifest` implementations get wrapped in `SerializerV1Adapter` automatically, keep working, and write the same bytes. HOCON and `SerializationSetup` registrations don't change, and the public lookup APIs still return `Serializer`.
* The new `Akka.Serialization.V2` package ships a Roslyn source generator that writes MessagePack serializers for your messages at compile time: mark messages with `[AkkaSerializable]` and `[AkkaField(n)]`, declare a `[AkkaSerializer<T>("name", id)]` partial class, and register it with `CreateRegistration().CreateSetup()`. No reflection, and the analyzer reports schema mistakes as build errors. It depends on MessagePack 3.1.8, past the 3.1.7 fix for [CVE-2026-48109](https://github.com/advisories/GHSA-hv8m-jj95-wg3x).
* Akka's own Cluster, DistributedData, Sharding and Delivery messages keep their existing protobuf serializers in this beta. Only Artery's control messages use the generator today ([#8334](https://github.com/akkadotnet/akka.net/pull/8334)).

**Artery TCP remoting (experimental, off by default)**

* Akka.Remote gains Artery, a new TCP remoting transport built on Akka.Streams. It runs beside classic DotNetty remoting, which stays the default. Turn it on with `akka.remote.artery.enabled = on`. Akka.NET logs a warning at startup that it is experimental; don't use it in production.
* Artery has its own handshake, a dedicated control stream, reliable system-message delivery, optional inbound and outbound lanes (`akka.remote.artery.advanced.inbound-lanes` / `outbound-lanes`) and a separate stream for large messages (`akka.remote.artery.large-message-destinations`). No compression yet.
* Artery is not wire-compatible with classic remoting. It uses the `akka://` scheme and port 25520 by default, so every node in a cluster must run the same transport and your seed-node addresses change. `tcp` is the only transport, and there's no Akka.Hosting helper yet; configure it through HOCON.

**Akka.IO**

* `TcpConnection` now runs on `Stream` + `System.IO.Pipelines` instead of `SocketAsyncEventArgs`. With the `ReadOnlySequence<byte>` API, framing decode allocates less. ([#8132](https://github.com/akkadotnet/akka.net/pull/8132))
* `WriteAck` now means the write is in the connection's output buffer and that buffer is below its resume mark, not that the bytes reached the socket. ([#8646](https://github.com/akkadotnet/akka.net/pull/8646))
* `akka.io.tcp.write-commands-queue-max-size` now caps a single write instead of the backlog, so use ack-based writes for backpressure. ([#8132](https://github.com/akkadotnet/akka.net/pull/8132), [#8646](https://github.com/akkadotnet/akka.net/pull/8646))
* `Tcp.ResumeWriting`, `Tcp.WritingResumed` and the `useResumeWriting` parameter are removed; they never did anything. ([#8660](https://github.com/akkadotnet/akka.net/pull/8660))
* TCP connects to a `DnsEndPoint` use the OS resolver, not `akka.io.dns`. ([#8132](https://github.com/akkadotnet/akka.net/pull/8132))
* UDP no longer pools buffers. If you pointed `buffer-pool` at `direct-buffer-pool` or a custom pool, move its `buffer-size` to `disabled-buffer-pool.buffer-size`, or datagrams over 512 bytes are truncated. ([#8606](https://github.com/akkadotnet/akka.net/pull/8606))
* `akka.io.tcp.maximum-frame-size` keeps its 1.5 meaning on the new transport: it caps bytes per socket read and per socket send. ([#8663](https://github.com/akkadotnet/akka.net/pull/8663))
* A `Tcp.Close` sent while a `ConfirmedClose` is draining now upgrades it to a full close instead of being dropped ([#8636](https://github.com/akkadotnet/akka.net/pull/8636)). If the drain fails during a close, the connection now reports `ErrorClosed` instead of the requested close event ([#8630](https://github.com/akkadotnet/akka.net/pull/8630)).

**Akka.Hosting now ships from this repository**

* `Akka.Hosting`, `Akka.Remote.Hosting`, `Akka.Cluster.Hosting`, `Akka.Persistence.Hosting`, `Akka.Hosting.TestKit` and `Akka.Hosting.TestKit.Xunit2` now live under `src/contrib/hosting` and ship at the Akka.NET version (forward-port of [#8591](https://github.com/akkadotnet/akka.net/pull/8591)). Package ids and namespaces stay the same. Bump `Akka.Hosting.*` to the same version as `Akka.*`; Hosting-only versions no longer exist. The [akkadotnet/Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting) repository keeps history up to 1.5.71 and will be archived.
* On 1.6 the Hosting packages target `net10.0` only and need `Microsoft.Extensions.*` 10.0 or later (Akka.Hosting 1.5.71 needed 9.0). Akka.Hosting's `OpenTelemetry` dependency moves to 1.15.3 or later, which clears [GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j).
* Hosting API changes: `WithExtension<T>()` / `WithExtensions(...)` now register through `ExtensionsSetup`, so `akka.extensions` no longer lists those extensions, and an extension id whose constructor throws now fails startup with a `ConfigurationException` (1.5 logged and skipped it). Read `Settings.Setup.Get<ExtensionsSetup>()` or call `HasExtension<T>()` instead of reading `akka.extensions`. `WithExtensions(params Type[])` is now marked `[RequiresUnreferencedCode]` - prefer `WithExtension<T>()`. `AddAkka<T>`, `WithHealthCheck<T>`, `WithDefaultLogMessageFormatter<T>` and `WithExtension<T>` carry new trimming attributes, so apps that run the trim analyzer can see new warnings at those call sites. ([#8649](https://github.com/akkadotnet/akka.net/pull/8649), [#8655](https://github.com/akkadotnet/akka.net/pull/8655))

**Other fixes and changes**

* Akka.Cluster: a node that is `Down` can no longer become leader of its own view, remove itself and keep running outside the cluster. ([#8652](https://github.com/akkadotnet/akka.net/pull/8652), forward-port of [#8650](https://github.com/akkadotnet/akka.net/pull/8650))
* Akka.Cluster: gossip keeps tombstones for removed members, so they can't come back; new setting `akka.cluster.prune-gossip-tombstones-after` (default `24h`). 1.5 nodes ignore the new field. ([#8484](https://github.com/akkadotnet/akka.net/pull/8484))
* Akka.Cluster.Metrics: a remote-deployed `AdaptiveLoadBalancingPool` with a non-default `MetricsSelector` now deserializes. ([#8704](https://github.com/akkadotnet/akka.net/pull/8704))
* Akka.DistributedData: `ReplicatorMessageSerializer` no longer starts a timer that nothing cancels. Its cache never worked and is gone; `serializer-cache-time-to-live` is still accepted but unused. ([#8699](https://github.com/akkadotnet/akka.net/pull/8699))
* Akka.Remote: classic remoting half-closes the socket on disassociate, so a Windows peer no longer loses the last frames we sent to a TCP reset. ([#8635](https://github.com/akkadotnet/akka.net/pull/8635))
* Akka.Remote: classic remoting decodes inbound envelopes with fewer allocations. No wire change. ([#8272](https://github.com/akkadotnet/akka.net/pull/8272))
* Akka.Streams: fixed a lost wakeup that could stall a `MergeHub`. ([#8665](https://github.com/akkadotnet/akka.net/pull/8665))
* Serialization: the JSON serializer no longer crashes the process with a stack overflow on an `ISurrogated` member that has no `$type` metadata. ([#8358](https://github.com/akkadotnet/akka.net/pull/8358))
* 1.6.0-beta1 also includes the fixes and features shipped in 1.5.x up to 1.5.71, such as logging context enrichment and scopes (1.5.60), cancellation-aware `Source.Queue` offers (1.5.69) and the consistent-hashing router collision fix for [#8031](https://github.com/akkadotnet/akka.net/issues/8031) (1.5.70).

#### 1.5.47 August 12th, 2025 ####

Akka.NET v1.5.47 is a minor patch containing several stability improvements to Akka.TestKit.

* [TestKit: Replace Thread.Sleep with SpinWait](https://github.com/akkadotnet/akka.net/pull/7745)
* [TestKit: Fix excessive AggregateException nesting when cancelling ExpectMessageAsync](https://github.com/akkadotnet/akka.net/pull/7747)
* [TestKit: Add async overload to multi-node TestConductor API](https://github.com/akkadotnet/akka.net/pull/7750)
* [Core: Move ByteBuffer alias to global using](https://github.com/akkadotnet/akka.net/pull/7681)

4 contributors since release 1.5.46

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 7       | 4185 | 3156 | Aaron Stannard      |
| 5       | 352  | 142  | Gregorius Soedharmo |
| 1       | 2    | 2    | dependabot[bot]     |
| 1       | 13   | 22   | Simon Cropp         |

To [see the full set of changes in Akka.NET v1.5.47, click here](https://github.com/akkadotnet/akka.net/milestone/130?closed=1)

#### 1.5.46 July 17th, 2025 ####

Akka.NET v1.5.46 is a minor patch containing a fix for the Akka.IO.Dns extension.

* [Core: Resolve ManagerClass type from IDnsProvider](https://github.com/akkadotnet/akka.net/pull/7727)

3 contributors since release 1.5.45

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 1       | 4    | 0    | Aaron Stannard      |
| 1       | 1    | 1    | Pavel Anpin         |
| 1       | 1    | 0    | Gregorius Soedharmo |

To [see the full set of changes in Akka.NET v1.5.46, click here](https://github.com/akkadotnet/akka.net/milestone/129?closed=1)

#### 1.5.45 July 7th, 2025 ####

Akka.NET v1.5.45 is a minor patch containing bug fixes for Core Akka and Akka.Cluster.Sharding plugin.

* [Core: Code modernization, use deconstructor for variable swapping](https://github.com/akkadotnet/akka.net/pull/7658)
* [Sharding: Fix unclean `ShardingConsumerControllerImpl` shutdown](https://github.com/akkadotnet/akka.net/pull/7714)
* [Core: Convert `Failure` to `Exception` for `Ask<object>`](https://github.com/akkadotnet/akka.net/pull/7286)
* [Core: Fix `Settings.InjectTopLevelFallback` race condition](https://github.com/akkadotnet/akka.net/pull/7721)
* [Sharding: Make remembered entities honor supervision strategy decisions](https://github.com/akkadotnet/akka.net/pull/7720)

**Supervision Strategy For Sharding Remembered Entities**

* We've added a `SupervisorStrategy` property to `ClusterShardingSettings`. You can use any type of `SupervisionStrategy`, but it is recommended that you inherit `ShardSupervisionStrategy` if you're making your own custom supervision strategy.
* Remembered shard entities will now honor `SupervisionStrategy` decisions and stops remembered entities if the `SupervisionStrategy.Decider` returned a `Directive.Stop` or if there is a maximum restart retry limitation.

4 contributors since release 1.5.44

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 10      | 823  | 108  | Gregorius Soedharmo |
| 1       | 7    | 13   | Simon Cropp         |
| 1       | 60   | 18   | ondravondra         |
| 1       | 1    | 0    | Aaron Stannard      |

To [see the full set of changes in Akka.NET v1.5.45, click here](https://github.com/akkadotnet/akka.net/milestone/128?closed=1)

#### 1.5.44 June 19th, 2025 ####

Akka.NET v1.5.44 is a minor patch that contains a bug fix to the Akka.Persistence plugin.

* [Persistence: Make sure that EventSourced timer is canceled when persistent actor is stopped](https://github.com/akkadotnet/akka.net/pull/7693)

3 contributors since release 1.5.43

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 10      | 438  | 323  | Gregorius Soedharmo |
| 2       | 4    | 2015 | Aaron Stannard      |
| 1       | 47   | 43   | Simon Cropp         |

To [see the full set of changes in Akka.NET v1.5.44, click here](https://github.com/akkadotnet/akka.net/milestone/127?closed=1).

#### 1.5.43 June 10th, 2025 ####

Akka.NET v1.5.43 contains several bug fixes and also adds new quality of life features.

* [Cluster.Tools: Fix PublishWithAck response message type](https://github.com/akkadotnet/akka.net/pull/7673)
* [Sharding: Allows sharding delivery consumer to passivate self](https://github.com/akkadotnet/akka.net/pull/7670)
* [TestKit: Fix CallingThreadDispatcher async context switching](https://github.com/akkadotnet/akka.net/pull/7674)
* [Persistence.Query: Add non-generic `ReadJournalFor` API method](https://github.com/akkadotnet/akka.net/pull/7679)
* [Core: Simplify null checks](https://github.com/akkadotnet/akka.net/pull/7659)
* [Core: Propagate CoordinatedShutdown reason to application exit code](https://github.com/akkadotnet/akka.net/pull/7684)
* [Core: Bump AkkaAnalyzerVersion to 0.3.3](https://github.com/akkadotnet/akka.net/pull/7685)
* [Core: Improve IScheduledTellMsg DeadLetter log message](https://github.com/akkadotnet/akka.net/pull/7686)

**New Akka.Analyzer Rules**

We've added three new Akka.Analyzer rules, AK2003, AK2004, and AK2005. All of them addresses the same Akka anti-pattern where a `void async` delegate is being passed into the `ReceiveActor.Receive<T>()` (AK2003), `IDslActor.Receive<T>()` (AK2004), and `ReceivePersistentActor.Command<T>()` (AK2005) message handlers.

Here are the documentation for each new rules:
* [AK2003 documentation](https://getakka.net/articles/debugging/rules/AK2003.html)
* [AK2004 documentation](https://getakka.net/articles/debugging/rules/AK2004.html)
* [AK2005 documentation](https://getakka.net/articles/debugging/rules/AK2005.html)

4 contributors since release 1.5.42

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 7       | 435  | 19   | Gregorius Soedharmo |
| 2       | 26   | 23   | Mark Dinh           |
| 1       | 49   | 136  | Simon Cropp         |
| 1       | 4    | 0    | Aaron Stannard      |

To [see the full set of changes in Akka.NET v1.5.43, click here](https://github.com/akkadotnet/akka.net/milestone/126?closed=1).
