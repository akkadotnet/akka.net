#### 1.5.0-alpha4 February 1st 2022 ####
Version 1.5.0-alpha3 contains several bug fixes and new features to Akka.NET

* [Akka.TestKit: Remove Akka.Tests.Shared.Internal dependency](https://github.com/akkadotnet/akka.net/pull/6258)
* [Akka.TestKit: Added ReceiveAsync feature to TestActorRef](https://github.com/akkadotnet/akka.net/pull/6281)
* [Akka.Stream: Fix `IAsyncEnumerator.DisposeAsync` bug](https://github.com/akkadotnet/akka.net/pull/6296)
* [Akka: Add `Exception` serialization support for built-in messages](https://github.com/akkadotnet/akka.net/pull/6300)
* [Akka: Add simple actor telemetry feature](https://github.com/akkadotnet/akka.net/pull/6299)
* [Akka.Streams: Move Channel stages from Alpakka to Akka.NET repo](https://github.com/akkadotnet/akka.net/pull/6268)
* [Akka: Set actor stash capacity to actor mailbox or dispatcher size](https://github.com/akkadotnet/akka.net/pull/6323)
* [Akka: Add `ByteString` support to copy to/from `Memory` and `Span`](https://github.com/akkadotnet/akka.net/pull/6026)
* [Akka: Add support for `UnrestrictedStash`](https://github.com/akkadotnet/akka.net/pull/6325)
* [Akka: Add API for `UntypedActorWithStash`](https://github.com/akkadotnet/akka.net/pull/6327)
* [Akka.Persistence.Sql.Common: Fix unhandled `DbExceptions` that are wrapped inside `AggregateException`](https://github.com/akkadotnet/akka.net/pull/6361)
* [Akka.Persistence.Sql: Fix persistence id publisher actor hung on failure messages](https://github.com/akkadotnet/akka.net/pull/6374)
* [Akka: Change default pool router supervisor strategy to `Restart`](https://github.com/akkadotnet/akka.net/pull/6370)
* NuGet package upgrades:
  * [Bump Microsoft.Data.SQLite from 6.0.10 to 7.0.2](https://github.com/akkadotnet/akka.net/pull/6339)
  * [Bump Google.Protobuf from 3.21.9 to 3.21.12](https://github.com/akkadotnet/akka.net/pull/6311)
  * [Bump Newtonsoft.Json from 9.0.1 to 13.0.1](https://github.com/akkadotnet/akka.net/pull/6303)
  * [Bump Microsoft.Extensions.ObjectPool from 6.0.10 to 7.0.2](https://github.com/akkadotnet/akka.net/pull/6340)
  * [Bump Microsoft.Extensions.DependencyInjection from 6.0.1 to 7.0.0](https://github.com/akkadotnet/akka.net/pull/6234)

If you want to see the [full set of changes made in Akka.NET v1.5.0 so far, click here](https://github.com/akkadotnet/akka.net/milestone/7?closed=1).

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 27      | 30   | 30   | dependabot[bot]     |
| 11      | 2212 | 165  | Gregorius Soedharmo |
| 4       | 741  | 208  | Ismael Hamed        |
| 4       | 680  | 112  | Aaron Stannard      |
| 3       | 87   | 178  | Sergey Popov        |
| 1       | 843  | 0    | Drew                |
| 1       | 2    | 2    | Popov Sergey        |

#### 1.5.0-alpha3 November 15th 2022 ####
Akka.NET v1.5.0-alpha3 is a security patch for Akka.NET v1.5.0-alpha2 but also includes some other fixes.

**Security Advisory**: Akka.NET v1.5.0-alpha2 and earlier depend on an old System.Configuration.ConfigurationManager version 4.7.0 which transitively depends on System.Common.Drawing v4.7.0. The System.Common.Drawing v4.7.0 is affected by a remote code execution vulnerability [GHSA-ghhp-997w-qr28](https://github.com/advisories/GHSA-ghhp-997w-qr28).

We have separately created a security advisory for [Akka.NET Versions < 1.4.46 and < 1.5.0-alpha3 to track this issue](https://github.com/akkadotnet/akka.net/security/advisories/GHSA-gpv5-rp6w-58r8).

**Fixes and Updates**

* [Akka: Revert ConfigurationException due to binary incompatibility](https://github.com/akkadotnet/akka.net/pull/6204)
* [Akka: Upgrade to Newtonsoft.Json 13.0.1 as minimum version](https://github.com/akkadotnet/akka.net/pull/6230) - resolves security issue.
* [Akka: Upgrade to System.Configuration.ConfigurationManager 6.0.1](https://github.com/akkadotnet/akka.net/pull/6229) - resolves security issue. 
* [Akka: Upgrade to Google.Protobuf 3.21.9](https://github.com/akkadotnet/akka.net/pull/6217)
* [Akka.Cluster.Tools: Make sure that `DeadLetter`s published by `DistributedPubSubMediator` contain full context of topic](https://github.com/akkadotnet/akka.net/pull/6212)
* [Akka.Streams: Remove suspicious code fragment in ActorMaterializer](https://github.com/akkadotnet/akka.net/pull/6216)
* [Akka.IO: Report cause for Akka/IO TCP `CommandFailed` events](https://github.com/akkadotnet/akka.net/pull/6221)
* [Akka.Cluster.Metrics: Improve CPU/Memory metrics collection at Akka.Cluster.Metrics](https://github.com/akkadotnet/akka.net/pull/6225) - built-in metrics are now much more accurate.

You can see the [full set of tracked issues for Akka.NET v1.5.0 here](https://github.com/akkadotnet/akka.net/milestone/7).

#### 1.5.0-alpha2 October 17th 2022 ####
Akka.NET v1.5.0-alpha2 is a maintenance release for Akka.NET v1.5 that contains numerous performance improvements in critical areas, including core actor message processing and Akka.Remote.

**Performance Fixes**

* [remove delegate allocation from `ForkJoinDispatcher` and `DedicatedThreadPool`](https://github.com/akkadotnet/akka.net/pull/6162)
* [eliminate `Mailbox` delegate allocations](https://github.com/akkadotnet/akka.net/pull/6162)
* [Reduce `FSM<TState, TData>` allocations](https://github.com/akkadotnet/akka.net/pull/6162)
* [removed boxing allocations inside `FSM.State.Equals`](https://github.com/akkadotnet/akka.net/pull/6196)
* [Eliminate `DefaultLogMessageFormatter` allocations](https://github.com/akkadotnet/akka.net/pull/6166)

In sum you should expect to see total memory consumption, garbage collection, and throughput improve when you upgrade to Akka.NET v1.5.0-alpha2.

**Other Features and Improvements**

* [DData: Suppress gossip message from showing up in debug log unless verbose debug logging is turned on](https://github.com/akkadotnet/akka.net/pull/6089)
* [TestKit: TestKit automatically injects the default TestKit default configuration if an ActorSystem is passed into its constructor](https://github.com/akkadotnet/akka.net/pull/6092)
* [Sharding: Added a new `GetEntityLocation` query message to retrieve an entity address location in the shard region](https://github.com/akkadotnet/akka.net/pull/6107)
* [Sharding: Fixed `GetEntityLocation` uses wrong actor path](https://github.com/akkadotnet/akka.net/pull/6121)
* [Akka.Cluster and Akka.Cluster.Sharding: should throw human-friendly exception when accessing cluster / sharding plugins when clustering is not running](https://github.com/akkadotnet/akka.net/pull/6169)
* [Akka.Cluster.Sharding: Add `HashCodeMessageExtractor` factory](https://github.com/akkadotnet/akka.net/pull/6173)
* [Akka.Persistence.Sql.Common: Fix `DbCommand.CommandTimeout` in `BatchingSqlJournal`](https://github.com/akkadotnet/akka.net/pull/6175)


#### 1.5.0-alpha1 August 22 2022 ####
Akka.NET v1.5.0-alpha1 is a major release that contains a lot of code improvement and rewrites/refactors. **Major upgrades to Akka.Cluster.Sharding in particular**.

__Deprecation__

Some codes and packages are being deprecated in v1.5
* [Deprecated/removed Akka.DI package](https://github.com/akkadotnet/akka.net/pull/6003)
  Please use the new `Akka.DependencyInjection` NuGet package as a replacement. Documentation can be read [here](https://getakka.net/articles/actors/dependency-injection.html)
* [Deprecated/removed Akka.MultiNodeTestRunner package](https://github.com/akkadotnet/akka.net/pull/6002)
  Please use the new `Akka.MultiNode.TestAdapter` NuGet package as a replacement. Documentation can be read [here](https://getakka.net/articles/testing/multi-node-testing.html).
* [Streams] [Refactor `SetHandler(Inlet, Outlet, IanAndOutGraphStageLogic)` to `SetHandlers()`](https://github.com/akkadotnet/akka.net/pull/5931)

__Changes__

__Akka__

* [Add dual targetting to support .NET 6.0](https://github.com/akkadotnet/akka.net/pull/5926)
  All `Akka.NET` packages are now dual targetting netstandard2.0 and net6.0 platforms, we will be integrating .NET 6.0 better performing API and SDK in the future.
* [Add `IThreadPoolWorkItem` support to `ThreadPoolDispatcher`](https://github.com/akkadotnet/akka.net/pull/5943)
* [Add `ValueTask` support to `PipeTo` extensions](https://github.com/akkadotnet/akka.net/pull/6025)
* [Add `CancellationToken` support to `Cancelable`](https://github.com/akkadotnet/akka.net/pull/6032)
* [Fix long starting loggers crashing `ActorSystem` startup](https://github.com/akkadotnet/akka.net/pull/6053)
  All loggers are asynchronously started during `ActorSystem` startup. A warning will be logged if a logger does not respond within the prescribed `akka.logger-startup-timeout` period and will be awaited upon in a detached task until the `ActorSystem` is shut down. This have a side effect in that slow starting loggers might not be able to capture all log events emmited by the `EventBus` until it is ready.

__Akka.Cluster__

* [Fix `ChannelTaskScheduler` to work with Akka.Cluster, ported from 1.4](https://github.com/akkadotnet/akka.net/pull/5920)
* [Harden `Cluster.JoinAsync()` and `Cluster.JoinSeedNodesAsync()` methods](https://github.com/akkadotnet/akka.net/pull/6033)
* [Fix `ShardedDaemonProcess` should use lease, if configured](https://github.com/akkadotnet/akka.net/pull/6058)
* [Make `SplitBrainResolver` more tolerant to invalid node records](https://github.com/akkadotnet/akka.net/pull/6064)
* [Enable `Heartbeat` and `HearbeatRsp` message serialization and deserialization](https://github.com/akkadotnet/akka.net/pull/6063)
  By default, `Akka.Cluster` will now use the new `Heartbeat` and `HartbeatRsp` message serialization/deserialization that was introduced in version 1.4.19. If you're doing a rolling upgrade from a version older than 1.4.19, you will need to set `akka.cluster.use-legacy-heartbeat-message` to true.

__Akka.Cluster.Sharding__

* [Make Cluster.Sharding recovery more tolerant against corrupted persistence data](https://github.com/akkadotnet/akka.net/pull/5978)
* [Major reorganization to Akka.Cluster.Sharding](https://github.com/akkadotnet/akka.net/pull/5857)

The Akka.Cluster.Sharding changes in Akka.NET v1.5 are significant, but backwards compatible with v1.4 and upgrades should happen seamlessly.

Akka.Cluster.Sharding's `state-store-mode` has been split into two parts:

* CoordinatorStore
* ShardStore

Which can use different persistent mode configured via `akka.cluster.sharding.state-store-mode` & `akka.cluster.sharding.remember-entities-store`.

Possible combinations:

state-store-mode | remember-entities-store | CoordinatorStore mode | ShardStore mode
------------------ | ------------------------- | ------------------------ | ------------------
persistence (default) | - (ignored) | persistence | persistence
ddata | ddata | ddata | ddata
ddata | eventsourced (new) | ddata | persistence

There should be no breaking changes from user perspective. Only some internal messages/objects were moved.
There should be no change in the `PersistentId` behavior and default persistent configuration (`akka.cluster.sharding.state-store-mode`)

This change is designed to speed up the performance of Akka.Cluster.Sharding coordinator recovery by moving `remember-entities` recovery into separate actors - this also solves major performance problems with the `ddata` recovery mode overall.

The recommended settings for maximum ease-of-use for Akka.Cluster.Sharding going forward will be:

```
akka.cluster.sharding{
  state-store-mode = ddata
  remember-entities-store = eventsourced
}
```

However, for the sake of backwards compatibility the Akka.Cluster.Sharding defaults have been left as-is:

```
akka.cluster.sharding{
  state-store-mode = persistence
  # remember-entities-store (not set - also uses legacy Akka.Persistence)
}
```

Switching over to using `remember-entities-store = eventsourced` will cause an initial migration of data from the `ShardCoordinator`'s journal into separate event journals going forward - __this migration is irreversible__ without taking the cluster offline and deleting all Akka.Cluster.Sharding-related data from Akka.Persistence, so plan accordingly.

__Akka.Cluster.Tools__

* [Add typed `ClusterSingleton` support](https://github.com/akkadotnet/akka.net/pull/6050)
* [Singleton can use `Member.AppVersion` metadata to decide its host node during hand-over](https://github.com/akkadotnet/akka.net/pull/6065)
  `Akka.Cluster.Singleton` can use `Member.AppVersion` metadata when it is relocating the singleton instance. When turned on, new singleton instance will be created on the oldest node in the cluster with the highest `AppVersion` number. You can opt-in to this behavior by setting `akka.cluster.singleton.consider-app-version` to true.

__Akka.Persistence.Query__

* [Add `TimeBasedUuid` offset property](https://github.com/akkadotnet/akka.net/pull/5995)

__Akka.Remote__

* [Fix typo in HOCON SSL settings. Backward compatible with the old setting names](https://github.com/akkadotnet/akka.net/pull/5895)
* [Treat all exceptions thrown inside `EndpointReader` message dispatch as transient, Ported from 1.4](https://github.com/akkadotnet/akka.net/pull/5972)
* [Fix SSL enable HOCON setting](https://github.com/akkadotnet/akka.net/pull/6038)

__Akka.Streams__

* [Allow GroupBy sub-flow to re-create closed sub-streams, backported to 1.4](https://github.com/akkadotnet/akka.net/pull/5874)
* [Fix ActorRef source not completing properly, backported to 1.4](https://github.com/akkadotnet/akka.net/pull/5875)
* [Rewrite `ActorRefSink` as a `GraphStage`](https://github.com/akkadotnet/akka.net/pull/5920)
* [Add stream cancellation cause upstream propagation, ported from 1.4](https://github.com/akkadotnet/akka.net/pull/5949)
* [Fix `VirtualProcessor` subscription bug, ported from 1.4](https://github.com/akkadotnet/akka.net/pull/5950)
* [Refactor `Sink.Ignore` signature from `Task` to `Task<Done>`](https://github.com/akkadotnet/akka.net/pull/5973)
* [Add `SourceWithContext.FromTuples()` operator`](https://github.com/akkadotnet/akka.net/pull/5987)
* [Add `GroupedWeightedWithin` operator](https://github.com/akkadotnet/akka.net/pull/6000)
* [Add `IAsyncEnumerable` source](https://github.com/akkadotnet/akka.net/pull/6044)

__Akka.TestKit__

* [Rewrite Akka.TestKit to work asynchronously from the ground up](https://github.com/akkadotnet/akka.net/pull/5953)



#### 1.4.37 April 14 2022 ####
Akka.NET v1.4.37 is a minor release that contains some minor bug fixes.

* [Persistence.Query: Change AllEvents query failure log severity from Debug to Error](https://github.com/akkadotnet/akka.net/pull/5835)
* [Coordination: Harden LeaseProvider instance Activator exception handling](https://github.com/akkadotnet/akka.net/pull/5838)
* [Akka: Make ActorSystemImpl.Abort skip the CoordinatedShutdown check](https://github.com/akkadotnet/akka.net/pull/5839)

If you want to see the [full set of changes made in Akka.NET v1.4.37, click here](https://github.com/akkadotnet/akka.net/milestone/68?closed=1).

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 3       | 15   | 4    | Gregorius Soedharmo |
| 1       | 2    | 2    | dependabot[bot]     |

#### 1.4.36 April 4 2022 ####
Akka.NET v1.4.36 is a minor release that contains some bug fixes. Most of the changes have been aimed at improving our web documentation and code cleanup to modernize some of our code.

* [Akka: Bump Hyperion to 0.12.2](https://github.com/akkadotnet/akka.net/pull/5805)

__Bug fixes__:
* [Akka: Fix CoordinatedShutdown memory leak](https://github.com/akkadotnet/akka.net/pull/5816)
* [Akka: Fix TcpConnection error handling and death pact de-registration](https://github.com/akkadotnet/akka.net/pull/5817)

If you want to see the [full set of changes made in Akka.NET v1.4.36, click here](https://github.com/akkadotnet/akka.net/milestone/67?closed=1).

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 5       | 274  | 33   | Gregorius Soedharmo |
| 4       | 371  | 6    | Ebere Abanonu       |
| 3       | 9    | 3    | Aaron Stannard      |
| 1       | 34   | 38   | Ismael Hamed        |
| 1       | 2    | 3    | Adrian Leonhard     |

#### 1.4.35 March 18 2022 ####
Akka.NET v1.4.35 is a minor release that contains some bug fixes. Most of the changes have been aimed at improving our web documentation and code cleanup to modernize some of our code.

__Bug fixes__:
* [Akka: Fixed IActorRef leak inside EventStream](https://github.com/akkadotnet/akka.net/pull/5720)
* [Akka: Fixed ActorSystemSetup.And forgetting registered types](https://github.com/akkadotnet/akka.net/issues/5728)
* [Akka.Persistence.Query.Sql: Fixed Query PersistenceIds query bug](https://github.com/akkadotnet/akka.net/pull/5715)
* [Akka.Streams: Add MapMaterializedValue for SourceWithContext and FlowWithContext](https://github.com/akkadotnet/akka.net/pull/5711)

If you want to see the [full set of changes made in Akka.NET v1.4.35, click here](https://github.com/akkadotnet/akka.net/milestone/66?closed=1).

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 6       | 2178 | 174  | Aaron Stannard      |
| 2       | 43   | 33   | Gregorius Soedharmo |
| 1       | 71   | 19   | Ismael Hamed        |
| 1       | 1    | 1    | dependabot[bot]     |

#### 1.4.34 March 7 2022 ####
Akka.NET v1.4.34 is a minor release that contains some bug fixes. Most of the changes have been aimed at improving our web documentation and code cleanup to modernize some of our code. 

__Bug fixes__:
* [Akka: Added support to pass a state object into CircuitBreaker to reduce allocations](https://github.com/akkadotnet/akka.net/pull/5650)
* [Akka.DistributedData: ORSet merge operation performance improvement](https://github.com/akkadotnet/akka.net/pull/5686)
* [Akka.Streams: FlowWithContext generic type parameters have been reordered to make them easier to read](https://github.com/akkadotnet/akka.net/pull/5648)

__Improvements__:
* [Akka: PipeTo can be configured to retain async threading context](https://github.com/akkadotnet/akka.net/pull/5684)

If you want to see the [full set of changes made in Akka.NET v1.4.34, click here](https://github.com/akkadotnet/akka.net/milestone/65?closed=1).

| COMMITS | LOC+   | LOC-  | AUTHOR              |
|---------|--------|-------|---------------------|
| 12      | 1177   | 718   | Ebere Abanonu       |
| 6       | 192    | 47    | Gregorius Soedharmo |
| 3       | 255    | 167   | Ismael Hamed        |
| 1       | 3      | 0     | Aaron Stannard      |
| 1       | 126    | 10    | Drew                |

#### 1.4.33 February 14 2022 ####
Akka.NET v1.4.33 is a minor release that contains some bug fixes. Most of the changes have been aimed at improving our web documentation and code cleanup to modernize some of our code. The most important bug fix is the actor Props memory leak when actors are cached inside Akka.Remote. 

* [Akka: Fix memory leak bug within actor Props](https://github.com/akkadotnet/akka.net/pull/5556)
* [Akka: Fix ChannelExecutor configuration backward compatibility bug](https://github.com/akkadotnet/akka.net/pull/5568)
* [Akka.TestKit: Fix ExpectAsync detached Task bug](https://github.com/akkadotnet/akka.net/pull/5538)
* [DistributedPubSub: Fix DeadLetter suppression for topics with no subscribers](https://github.com/akkadotnet/akka.net/pull/5561)

If you want to see the [full set of changes made in Akka.NET v1.4.33, click here](https://github.com/akkadotnet/akka.net/milestone/64?closed=1).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 63 | 1264 | 1052 | Ebere Abanonu |
| 9 | 221 | 27 | Brah McDude |
| 8 | 2537 | 24 | Gregorius Soedharmo |
| 2 | 4 | 1 | Aaron Stannard |
| 1 | 2 | 2 | ignobilis |

#### 1.4.32 January 19 2022 ####
Akka.NET v1.4.32 is a minor release that contains some API improvements. Most of the changes have been aimed at improving our web documentation and code cleanup to modernize some of our code. One big improvement in this version release is the Hyperion serialization update. 

Hyperion 0.12.0 introduces a new deserialization security mechanism to allow users to selectively filter allowed types during deserialization to prevent deserialization of untrusted data described [here](https://cwe.mitre.org/data/definitions/502.html). This new feature is exposed in Akka.NET in HOCON through the new [`akka.actor.serialization-settings.hyperion.allowed-types`](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/serializers/Akka.Serialization.Hyperion/reference.conf#L33-L35) settings or programmatically through the new `WithTypeFilter` method in the `HyperionSerializerSetup` class.

The simplest way to programmatically describe the type filter is to use the convenience class `TypeFilterBuilder`:

```c#
var typeFilter = TypeFilterBuilder.Create()
    .Include<AllowedClassA>()
    .Include<AllowedClassB>()
    .Build();
var setup = HyperionSerializerSetup.Default
    .WithTypeFilter(typeFilter);
```

You can also create your own implementation of `ITypeFilter` and pass an instance of it into the `WithTypeFilter` method.

For complete documentation, please read the Hyperion [readme on filtering types for secure deserialization.](https://github.com/akkadotnet/Hyperion#whitelisting-types-on-deserialization)

* [Akka.Streams: Added Flow.LazyInitAsync and Sink.LazyInitSink to replace Sink.LazyInit](https://github.com/akkadotnet/akka.net/pull/5476)
* [Akka.Serialization.Hyperion: Implement the new ITypeFilter security feature](https://github.com/akkadotnet/akka.net/pull/5510)

If you want to see the [full set of changes made in Akka.NET v1.4.32, click here](https://github.com/akkadotnet/akka.net/milestone/63).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 11 | 1752 | 511 | Aaron Stannard |
| 8 | 1433 | 534 | Gregorius Soedharmo |
| 3 | 754 | 222 | Ismael Hamed |
| 2 | 3 | 6 | Brah McDude |
| 2 | 227 | 124 | Ebere Abanonu |
| 1 | 331 | 331 | Sean Killeen |
| 1 | 1 | 1 | TangkasOka |

#### 1.4.31 December 20 2021 ####
Akka.NET v1.4.31 is a minor release that contains some bug fixes.

Akka.NET v1.4.30 contained a breaking change that broke binary compatibility with all Akka.DI plugins.
Even though those plugins are deprecated that change is not compatible with our SemVer standards 
and needed to be reverted. We regret the error.

Bug fixes:
* [Akka: Reverted Props code refactor](https://github.com/akkadotnet/akka.net/pull/5454)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 9 | 2 | Gregorius Soedharmo |

#### 1.4.30 December 20 2021 ####
Akka.NET v1.4.30 is a minor release that contains some enhancements for Akka.Streams and some bug fixes.

New features:
* [Akka: Added StringBuilder pooling in NewtonsoftJsonSerializer](https://github.com/akkadotnet/akka.net/pull/4929)
* [Akka.TestKit: Added InverseFishForMessage](https://github.com/akkadotnet/akka.net/pull/5430)
* [Akka.Streams: Added custom frame sized Flow to Framing](https://github.com/akkadotnet/akka.net/pull/5444)
* [Akka.Streams: Allow Stream to be consumed as IAsyncEnumerable](https://github.com/akkadotnet/akka.net/pull/4742) 

Bug fixes:
* [Akka.Cluster: Reverted startup sequence change](https://github.com/akkadotnet/akka.net/pull/5437)

If you want to see the [full set of changes made in Akka.NET v1.4.30, click here](https://github.com/akkadotnet/akka.net/milestone/61).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 6 | 75 | 101 | Aaron Stannard |
| 2 | 53 | 5 | Brah McDude |
| 2 | 493 | 12 | Drew |
| 1 | 289 | 383 | Andreas Dirnberger |
| 1 | 220 | 188 | Gregorius Soedharmo |
| 1 | 173 | 28 | Ismael Hamed |

#### 1.4.29 December 13 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.29 is a minor release that contains some enhancements for Akka.Streams and some bug fixes.

New features:
* [Akka: Added a channel based task scheduler](https://github.com/akkadotnet/akka.net/pull/5403)
* [Akka.Discovery: Moved Akka.Discovery out of beta](https://github.com/akkadotnet/akka.net/pull/5380)

Documentation:
* [Akka: Added a serializer ID troubleshooting table](https://github.com/akkadotnet/akka.net/pull/5418)
* [Akka.Cluster.Sharding: Added a tutorial section](https://github.com/akkadotnet/akka.net/pull/5421)

Bug fixes:
* [Akka.Cluster: Changed Akka.Cluster startup sequence](https://github.com/akkadotnet/akka.net/pull/5398)
* [Akka.DistributedData: Fix LightningDB throws MDB_NOTFOUND when data directory already exist](https://github.com/akkadotnet/akka.net/pull/5424)
* [Akka.IO: Fix memory leak on UDP connector](https://github.com/akkadotnet/akka.net/pull/5404)
* [Akka.Persistence.Sql: Fix performance issue with highest sequence number query](https://github.com/akkadotnet/akka.net/pull/5420)

If you want to see the [full set of changes made in Akka.NET v1.4.29, click here](https://github.com/akkadotnet/akka.net/milestone/60).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 7 | 82 | 51 | Aaron Stannard |
| 6 | 1381 | 483 | Gregorius Soedharmo |
| 4 | 618 | 85 | Andreas Dirnberger |
| 1 | 4 | 4 | Luca V |
| 1 | 1 | 1 | dependabot[bot] |

#### 1.4.28 November 10 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.28 is a minor release that contains some enhancements for Akka.Streams and some bug fixes.

**New Akka.Streams Stages**
Akka.NET v1.4.28 includes two new Akka.Streams stages:

* [`Source.Never`](https://getakka.net/articles/streams/builtinstages.html#never) - a utility stage that never emits any elements, never completes, and never fails. Designed primarily for unit testing.
* [`Flow.WireTap`](https://getakka.net/articles/streams/builtinstages.html#wiretap) - the `WireTap` stage attaches a given `Sink` to a `Flow` without affecting any of the upstream or downstream elements. This stage is designed for performance monitoring and instrumentation of Akka.Streams graphs.

In addition to these, here are some other changes introduced Akka.NET v1.4.28:

* [Akka.Streams: `Source` that flattens a `Task` source and keeps the materialized value](https://github.com/akkadotnet/akka.net/pull/5338)
* [Akka.Streams: made `GraphStageLogic.LogSource` virtual and change default `StageLogic` `LogSource`](https://github.com/akkadotnet/akka.net/pull/5360)
* [Akka.IO: `UdpListener` Responds IPv6 Bound message with IPv4 Bind message](https://github.com/akkadotnet/akka.net/issues/5344)
* [Akka.MultiNodeTestRunner: now runs on Linux and as a `dotnet test` package](https://github.com/akkadotnet/Akka.MultiNodeTestRunner/releases/tag/1.0.0) - we will keep you posted on this, as we're still working on getting Rider / VS Code / Visual Studio debugger-attached support to work correctly.
* [Akka.Persistence.Sql.Common: Cancel `DBCommand` after finish reading events by PersistenceId ](https://github.com/akkadotnet/akka.net/pull/5311) - *massive* performance fix for Akka.Persistence with many log entries on SQL-based journals.
* [Akka.Actor: `DefaultResizer` does not reisize when `ReceiveAsync` is used](https://github.com/akkadotnet/akka.net/issues/5327)

If you want to see the [full set of changes made in Akka.NET v1.4.28, click here](https://github.com/akkadotnet/akka.net/milestone/59).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 16 | 2707 | 1911 | Sean Killeen |
| 8 | 1088 | 28 | Ismael Hamed |
| 6 | 501 | 261 | Gregorius Soedharmo |
| 5 | 8 | 8 | dependabot[bot] |
| 4 | 36 | 86 | Aaron Stannard |
| 1 | 1 | 0 | Jarl Sveinung Flø Rasmussen |

Special thanks for @SeanKilleen for contributing extensive Markdown linting and automated CI checks for that to our documentation! https://github.com/akkadotnet/akka.net/issues/5312

#### 1.4.27 October 11 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.27 is a small release that contains some _major_ performance improvements for Akka.Remote.

**Performance Fixes**
In [RemoteActorRefProvider address paring, caching and resolving improvements](https://github.com/akkadotnet/akka.net/pull/5273) Akka.NET contributor @Zetanova introduced some major changes that make the entire `ActorPath` class much more reusable and more parse-efficient.

Our last major round of Akka.NET performance improvements in Akka.NET v1.4.25 produced the following:

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0
ProcessorCount:                    16
ClockSpeed:                        0 MHZ
Actor Count:                       32
Messages sent/received per client: 200000  (2e5)
Is Server GC:                      True
Thread count:                      111

Num clients, Total [msg], Msgs/sec, Total [ms]
         1,  200000,    130634,    1531.54
         5, 1000000,    246975,    4049.20
        10, 2000000,    244499,    8180.16
        15, 3000000,    244978,   12246.39
        20, 4000000,    245159,   16316.37
        25, 5000000,    243333,   20548.09
        30, 6000000,    241644,   24830.55
```

In Akka.NET v1.4.27 those numbers now look like:

```
OSVersion:                         Microsoft Windows NT 6.2.9200.
ProcessorCount:                    16                            
ClockSpeed:                        0 MHZ                         
Actor Count:                       32                            
Messages sent/received per client: 200000  (2e5)                 
Is Server GC:                      True                          
Thread count:                      111                           
                                                                 
Num clients, Total [msg], Msgs/sec, Total [ms]                   
         1,  200000,    105043,    1904.29                       
         5, 1000000,    255494,    3914.73                       
        10, 2000000,    291843,    6853.30                       
        15, 3000000,    291291,   10299.75                       
        20, 4000000,    286513,   13961.68                       
        25, 5000000,    292569,   17090.64                       
        30, 6000000,    281492,   21315.35
```

To put these numbers in comparison, here's what Akka.NET's performance looked like as of v1.4.0:

```
Num clients (actors)    Total [msg] Msgs/sec    Total [ms]
1   200000  69736   2868.60
5   1000000 141243  7080.98
10  2000000 136771  14623.27
15  3000000 38190   78556.49
20  4000000 32401   123454.60
25  5000000 33341   149967.08
30  6000000 126093  47584.92
```


We've made Akka.Remote consistently faster, more predictable, and reduced total memory consumption significantly in the process.


You can [see the full set of changes introduced in Akka.NET v1.4.27 here](https://github.com/akkadotnet/akka.net/milestone/57?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 89 | 8 | Aaron Stannard |
| 1 | 856 | 519 | Andreas Dirnberger |
| 1 | 3 | 4 | Vadym Artemchuk |
| 1 | 261 | 233 | Gregorius Soedharmo |
| 1 | 1 | 1 | dependabot[bot] |

#### 1.4.26 September 28 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.26 is a very small release that addresses one wire format regression introduced in Akka.NET v1.4.20.

**Bug Fixes and Improvements**
* [Akka.Remote / Akka.Persistence: PrimitiveSerializers manifest backwards compatibility problem](https://github.com/akkadotnet/akka.net/issues/5279) - this could cause regressions when upgrading to Akka.NET v1.4.20 and later. We have resolved this issue in Akka.NET v1.4.26. [Please see our Akka.NET v1.4.26 upgrade advisory for details](https://getakka.net/community/whats-new/akkadotnet-v1.4-upgrade-advisories.html#upgrading-to-akkanet-v1426-from-older-versions).
* [Akka.DistributedData.LightningDb: Revert #5180, switching back to original LightningDB packages](https://github.com/akkadotnet/akka.net/pull/5286)

You can [see the full set of changes introduced in Akka.NET v1.4.26 here](https://github.com/akkadotnet/akka.net/milestone/57?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 99 | 96 | Gregorius Soedharmo |
| 3 | 79 | 5 | Aaron Stannard |
| 1 | 1 | 1 | dependabot[bot] |

#### 1.4.25 September 08 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.25 includes some _significant_ performance improvements for Akka.Remote and a number of important bug fixes and improvements.

**Bug Fixes and Improvements**
* [Akka.IO.Tcp: connecting to an unreachable DnsEndpoint never times out](https://github.com/akkadotnet/akka.net/issues/5154)
* [Akka.Actor: need to enforce `stdout-loglevel = off` all the way through ActorSystem lifecycle](https://github.com/akkadotnet/akka.net/issues/5246)
* [Akka.Actor: `Ask` should push unhandled answers into deadletter](https://github.com/akkadotnet/akka.net/pull/5259)
* [Akka.Routing: Make Router.Route` virtual](https://github.com/akkadotnet/akka.net/pull/5238)
* [Akka.Actor: Improve performance on `IActorRef.Child` API](https://github.com/akkadotnet/akka.net/pull/5242) - _signficantly_ improves performance of many Akka.NET functions, but includes a public API change on `IActorRef` that is source compatible but not necessarily binary-compatible. `IActorRef GetChild(System.Collections.Generic.IEnumerable<string> name)` is now `IActorRef GetChild(System.Collections.Generic.IReadOnlyList<string> name)`. This API is almost never called directly by user code (it's almost always called via the internals of the `ActorSystem` when resolving `ActorSelection`s or remote messages) so this change should be safe.
* [Akka.Actor: `IsNobody` throws NRE](https://github.com/akkadotnet/akka.net/issues/5213)
* [Akka.Cluster.Tools: singleton fix cleanup of overdue _removed members](https://github.com/akkadotnet/akka.net/pull/5229)
* [Akka.DistributedData: ddata exclude `Exiting` members in Read/Write `MajorityPlus`](https://github.com/akkadotnet/akka.net/pull/5227)

**Performance Improvements**
Using our standard `RemotePingPong` benchmark, the difference between v1.4.24 and v1.4.24 is significant:

_v1.4.24_

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0 
ProcessorCount:                    16                              
ClockSpeed:                        0 MHZ                           
Actor Count:                       32                              
Messages sent/received per client: 200000  (2e5)                   
Is Server GC:                      True                            
Thread count:                      111                             
                                                                   
Num clients, Total [msg], Msgs/sec, Total [ms]                     
         1,  200000,     96994,    2062.08                         
         5, 1000000,    194818,    5133.93                         
        10, 2000000,    198966,   10052.93                         
        15, 3000000,    199455,   15041.56                         
        20, 4000000,    198177,   20184.53                         
        25, 5000000,    197613,   25302.80                         
        30, 6000000,    197349,   30403.82                         
```

_v1.4.25_

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0
ProcessorCount:                    16
ClockSpeed:                        0 MHZ
Actor Count:                       32
Messages sent/received per client: 200000  (2e5)
Is Server GC:                      True
Thread count:                      111

Num clients, Total [msg], Msgs/sec, Total [ms]
         1,  200000,    130634,    1531.54
         5, 1000000,    246975,    4049.20
        10, 2000000,    244499,    8180.16
        15, 3000000,    244978,   12246.39
        20, 4000000,    245159,   16316.37
        25, 5000000,    243333,   20548.09
        30, 6000000,    241644,   24830.55
```

This represents a 24% overall throughput improvement in Akka.Remote across the board. We have additional PRs staged that should get aggregate performance improvements above 40% for Akka.Remote over v1.4.24 but they didn't make it into the Akka.NET v1.4.25 release.

You can [see the full set of changes introduced in Akka.NET v1.4.25 here](https://github.com/akkadotnet/akka.net/milestone/56?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 32 | 1301 | 400 | Aaron Stannard |
| 4 | 358 | 184 | Andreas Dirnberger |
| 3 | 414 | 149 | Gregorius Soedharmo |
| 3 | 3 | 3 | dependabot[bot] |
| 2 | 43 | 10 | zbynek001 |
| 1 | 14 | 13 | tometchy |
| 1 | 139 | 3 | carlcamilleri |

#### 1.4.24 August 17 2021 ####
**Maintenance Release for Akka.NET 1.4**

**Bug Fixes and Improvements**

* [Akka: Make `Router` open to extensions](https://github.com/akkadotnet/akka.net/pull/5201)
* [Akka: Allow null response to `Ask<T>`](https://github.com/akkadotnet/akka.net/pull/5205)
* [Akka.Cluster: Fix cluster startup race condition](https://github.com/akkadotnet/akka.net/pull/5185)
* [Akka.Streams: Fix RestartFlow bug](https://github.com/akkadotnet/akka.net/pull/5181)
* [Akka.Persistence.Sql: Implement TimestampProvider in BatchingSqlJournal](https://github.com/akkadotnet/akka.net/pull/5192)
* [Akka.Serialization.Hyperion: Bump Hyperion version from 0.11.0 to 0.11.1](https://github.com/akkadotnet/akka.net/pull/5206)
* [Akka.Serialization.Hyperion: Add Hyperion unsafe type filtering security feature](https://github.com/akkadotnet/akka.net/pull/5208)

You can [see the full set of changes introduced in Akka.NET v1.4.24 here](https://github.com/akkadotnet/akka.net/milestone/55?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 5 | 360 | 200 | Aaron Stannard |
| 3 | 4 | 4 | dependabot[bot] |
| 1 | 548 | 333 | Arjen Smits |
| 1 | 42 | 19 | Martijn Schoemaker |
| 1 | 26 | 27 | Andreas Dirnberger |
| 1 | 171 | 27 | Gregorius Soedharmo |

#### 1.4.23 August 09 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.23 is designed to patch an issue that occurs on Linux machines using Akka.Cluster.Sharding with `akka.cluster.sharding.state-store-mode=ddata` and `akka.cluster.sharding.remember-entities=on`: "[System.DllNotFoundException: Unable to load shared library 'lmdb' or one of its dependencies](https://github.com/akkadotnet/akka.net/issues/5174)"

In [Akka.NET v1.4.21 we added built-in support for Akka.DistributedData.LightningDb](https://github.com/akkadotnet/akka.net/releases/tag/1.4.21) for use with the `remember-entities` setting, but we never received any reports about this issue until shortly after v1.4.22 was released. Fundamentally, the problem was that our downstream dependency, Lightning.NET, doesn't include any of the necessary Linux native binaries in their distributions currently. So in the meantime, we've published our own "vendored" distribution of Lightning.NET to NuGet until a new official one is released that includes these binaries.

There are some other small [fixes included in Akka.NET v1.4.23 and you can read about them here](https://github.com/akkadotnet/akka.net/milestone/54).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 8 | 136 | 2803 | Aaron Stannard |
| 2 | 61 | 3 | Gregorius Soedharmo |

#### 1.4.22 August 05 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.22 is a fairly large release that includes an assortment of performance and bug fixes.

**Performance Fixes**
Akka.NET v1.4.22 includes a _significant_ performance improvement for `Ask<T>`, which now requires 1 internal `await` operation instead of 3:

*Before*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 83.313 ms | 0.7553 ms | 0.7065 ms | 4666.6667 |     - |     - |     19 MB |
|          CreateActorSelection |      10000 |  5.572 ms | 0.1066 ms | 0.1140 ms |  953.1250 |     - |     - |      4 MB |


*After*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 71.216 ms | 0.9885 ms | 0.9246 ms | 4285.7143 |     - |     - |     17 MB |
|          CreateActorSelection |      10000 |  5.462 ms | 0.0495 ms | 0.0439 ms |  953.1250 |     - |     - |      4 MB |

**Bug Fixes and Improvements**

* [Akka: Use ranged nuget versioning for Newtonsoft.Json](https://github.com/akkadotnet/akka.net/pull/5099)
* [Akka: Pipe of Canceled Tasks](https://github.com/akkadotnet/akka.net/pull/5123)
* [Akka: CircuitBreaker's Open state should return a faulted Task instead of throwing](https://github.com/akkadotnet/akka.net/issues/5117)
* [Akka.Remote: Can DotNetty socket exception include information about the address?](https://github.com/akkadotnet/akka.net/issues/5130)
* [Akka.Remote: log full exception upon deserialization failure](https://github.com/akkadotnet/akka.net/pull/5121)
* [Akka.Cluster: SBR fix & update](https://github.com/akkadotnet/akka.net/pull/5147)
* [Akka.Streams: Restart Source|Flow|Sink: Configurable stream restart deadline](https://github.com/akkadotnet/akka.net/pull/5122)
* [Akka.DistributedData: ddata replicator stops but doesn't look like it can be restarted easily](https://github.com/akkadotnet/akka.net/pull/5145)
* [Akka.DistributedData: ddata ReadMajorityPlus and WriteMajorityPlus](https://github.com/akkadotnet/akka.net/pull/5146)
* [Akka.DistributedData: DData Max-Delta-Elements may not be fully honoured](https://github.com/akkadotnet/akka.net/issues/5157)

You can [see the full set of changes introduced in Akka.NET v1.4.22 here](https://github.com/akkadotnet/akka.net/milestone/52)

**Akka.Cluster.Sharding.RepairTool**
In addition to the work done on Akka.NET itself, we've also created a separate tool for cleaning up any left-over data in the event of an Akka.Cluster.Sharding cluster running with `akka.cluster.sharding.state-store-mode=persistence` was terminated abruptly before it had a chance to cleanup.

We've added documentation to the Akka.NET website that explains how to use this tool here: https://getakka.net/articles/clustering/cluster-sharding.html#cleaning-up-akkapersistence-shard-state

And the tool itself has documentation here: https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool

| COMMITS | LOC+ | LOC- | AUTHOR |       
| --- | --- | --- | --- |                
| 16 | 1254 | 160 | Gregorius Soedharmo |
| 7 | 104 | 83 | Aaron Stannard |        
| 5 | 8 | 8 | dependabot[bot] |          
| 4 | 876 | 302 | Ismael Hamed |         
| 2 | 3942 | 716 | zbynek001 |           
| 2 | 17 | 3 | Andreas Dirnberger |      
| 1 | 187 | 2 | andyfurnival |           
| 1 | 110 | 5 | Igor Fedchenko |         
