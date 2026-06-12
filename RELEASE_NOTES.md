#### 1.5.69 June 12th, 2026 ####

Akka.NET v1.5.69 is a maintenance release with bug fixes for Akka.DistributedData state propagation, Akka.Core message rejection handling, and Akka.Streams tracing reliability and backpressure cancellation.

**Akka.Streams**
* [Fix: propagate trace context across the `.Async()` actor boundary](https://github.com/akkadotnet/akka.net/pull/8246) - Fixes [#8243](https://github.com/akkadotnet/akka.net/issues/8243): With stream tracing enabled, per-element trace context was dropped at the publisher/subscriber actor boundary introduced by `.Async()`, leaving downstream stages with no ambient `ActivityContext`. Trace context now flows correctly across fused interpreter shells.
* [Add `OfferAsync(T, CancellationToken)` to `ISourceQueue<T>`](https://github.com/akkadotnet/akka.net/pull/8248) — enables cancellation of backpressured pending offers without emitting the cancelled element.

**Akka.Streams Bug Fixes**
* [Fix: observe discarded stream task faults](https://github.com/akkadotnet/akka.net/pull/8242) - Fixes [#8241](https://github.com/akkadotnet/akka.net/issues/8241): Resolves a `NullReferenceException` in the `GraphInterpreter` when tracing across actor boundaries — the interpreter now safely handles null activity context references during stream teardown.

**Akka.Core**
* [Fix: `RejectOnType<TMessage>` should use `Rejection`, not `Failure`](https://github.com/akkadotnet/akka.net/pull/8231) - Fixes [#8231](https://github.com/akkadotnet/akka.net/issues/8231): `RejectOnType` now correctly wraps rejected messages as `Rejection` rather than throwing a `Failure`, matching the expected stream contract.

**Akka.DistributedData**
* [Fix: propagate full state after pruning](https://github.com/akkadotnet/akka.net/pull/8220) - Fixes [#8220](https://github.com/akkadotnet/akka.net/issues/8220): Resolves a bug where pruning could cause incomplete state propagation in `ORSet` and `LWWDictionary`, leading to data inconsistencies during node merges.

1 contributor since release 1.5.68

Akka.NET v1.5.68 is a maintenance release with bug fixes for Akka.IO TCP connection handling, Akka.Streams stream materialized task faults, and Akka.TestKit xUnit 3 parallel context management.

**Akka.IO Bug Fixes**

* [Fix: report `Tcp.CommandFailed` when a scheduled connect retry throws](https://github.com/akkadotnet/akka.net/pull/8214) - Fixes [#8195](https://github.com/akkadotnet/akka.net/issues/8195): On Linux, a dropped TCP connection could permanently stall the user actor — it never received `Tcp.Connected` or `Tcp.CommandFailed` because a `PlatformNotSupportedException` thrown during a scheduled connect retry was swallowed by the `HashedWheelTimerScheduler`. The retry is now scheduled as a `RetryConnect` self-message via `IWithTimers`, ensuring any exception is surfaced to the commander as `Tcp.CommandFailed` and the connection actor stops cleanly. The pending timer is also canceled automatically when the actor stops, removing a latent use-after-dispose bug.

**Akka.Streams Bug Fixes**

* [Fix: observe discarded stream task faults](https://github.com/akkadotnet/akka.net/pull/8212) - Fixes [#8209](https://github.com/akkadotnet/akka.net/issues/8209) and [#8210](https://github.com/akkadotnet/akka.net/issues/8210): `IgnoreSink`, `QueueSource`, and `LazySink` now observe their internal materialized `Task` faults, preventing them from surfacing later as `UnobservedTaskException` events on the thread pool.

**Akka.TestKit Bug Fixes**

* [Fix: wrap outer `SynchronizationContext` in `ActorCellKeepingSynchronizationContext`](https://github.com/akkadotnet/akka.net/pull/8182) - `ActorCellKeepingSynchronizationContext` now accepts an optional inner `SynchronizationContext` and delegates scheduling to it while wrapping callbacks with the cell-pinning window. This prevents test hangs in downstream consumers such as `Akka.Hosting.TestKit` whose async `IHost` lifecycle depends on xUnit v3's `MaxConcurrencySyncContext` scheduling.

1 contributor since release 1.5.67

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 476 | 119 | Aaron Stannard |

To see the full set of changes in Akka.NET v1.5.68, [click here](https://github.com/akkadotnet/akka.net/milestone/151?closed=1).

#### 1.5.67 April 25th, 2026 ####

Akka.NET v1.5.67 is a hotfix release that reverts a breaking change to the persistence plugin contract introduced in v1.5.66.

**Akka.Persistence: Revert async `WriteMessagesAsync`/`SaveAsync` dispatch ([#8163](https://github.com/akkadotnet/akka.net/pull/8163))**

v1.5.66 added `Task.Yield()` inside `AsyncWriteJournal.ExecuteBatch` and `SnapshotStore` to move persistence plugin `WriteMessagesAsync`/`SaveAsync` calls off the actor thread. While this improved throughput in benchmarks, it silently broke the implicit contract that persistence plugins rely on — that the synchronous preamble of these methods executes in actor context.

This caused failures in plugins that:
* Access `Self` inside `WriteMessagesAsync` (e.g. Akka.Persistence.Sql, Akka.Persistence.EventStore) — throws `NotSupportedException` off the actor thread
* Use non-thread-safe collections for write tracking (e.g. `Dictionary<string, Task>`) — concurrent access from actor thread and thread pool causes `InvalidOperationException`
* Send messages to subscribers after writes complete (e.g. Akka.Persistence.Redis) — accesses shared actor state off-thread

This release removes the `Task.Yield()` calls and restores the original dispatch behavior. A future version may reintroduce this optimization with a more targeted approach that preserves the plugin threading contract.

**If you are on v1.5.66**, upgrade to v1.5.67 immediately if you use any third-party persistence plugin.

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 3 | 17 | Aaron Stannard |

#### 1.5.66 April 24th, 2026 ####

Akka.NET v1.5.66 is a significant release with persistence bug fixes, major Akka.Streams improvements including OpenTelemetry trace propagation and non-blocking materialized values, and new serialization security controls.

**Akka.Streams: OpenTelemetry Trace Context Propagation**

Akka.Streams now propagates `System.Diagnostics.Activity` trace context end-to-end through stream graphs, including across async stage boundaries, fan-in merges, and fan-out broadcasts. This enables full distributed tracing visibility into stream pipelines when using OpenTelemetry.

For full documentation, see: https://getakka.net/articles/streams/stream-tracing.html

* [Akka.Streams: end-to-end OpenTelemetry trace context propagation](https://github.com/akkadotnet/akka.net/pull/8160)

**Akka.Streams: Non-Blocking Materialized-Value TaskCompletionSource**

All `TaskCompletionSource` instances used for materialized values across Akka.Streams now use `TaskCreationOptions.RunContinuationsAsynchronously`, eliminating potential deadlocks and thread-pool starvation when continuations run synchronously on completion.

* [Non-blocking materialized-value TaskCompletionSource](https://github.com/akkadotnet/akka.net/issues/8161)

**Akka.Persistence**
* [Redesign MemoryJournal and MemorySnapshotStore with channel-based drain-on-read pattern](https://github.com/akkadotnet/akka.net/pull/8184)
* [Ensure WriteMessagesAsync/SaveAsync is called asynchronously](https://github.com/akkadotnet/akka.net/pull/8163)

**Akka.Core**
* [Filter link-local (APIPA) addresses from DNS resolution results](https://github.com/akkadotnet/akka.net/pull/8178)
* [Fix multi-node adapter output race](https://github.com/akkadotnet/akka.net/pull/8180)

**New Features**
* [Add `allow-unregistered-types` serialization setting](https://github.com/akkadotnet/akka.net/pull/8173) — when set to `false`, `FindSerializerForType` throws `SerializationException` if no explicit serializer binding exists, rather than falling back to the default serializer.
* [Widen ActorTaskScheduler(ActorCell) ctor to protected internal](https://github.com/akkadotnet/akka.net/pull/8158)

**Documentation**
* [Surface serialization security guidance on remoting security page](https://github.com/akkadotnet/akka.net/pull/8177)
* [Replace TBD XML doc placeholders in Scheduler and Stash](https://github.com/akkadotnet/akka.net/pull/8119)

4 contributors since release 1.5.65

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 18 | 3962 | 991 | Aaron Stannard |
| 8 | 1627 | 1952 | Gregorius Soedharmo |
| 6 | 214 | 177 | Matt Kotsenas |
| 1 | 55 | 39 | schdooz |

To see the full set of changes in Akka.NET v1.5.66, [click here](https://github.com/akkadotnet/akka.net/milestone/149?closed=1).

#### 1.5.65 April 10th, 2026 ####

Akka.NET v1.5.65 is a maintenance release with important bug fixes for Akka.Cluster.Sharding, Akka.Core configuration, and Akka.TestKit.

**Akka.Cluster.Sharding Bug Fixes**

* [Fix cluster sharding lease coordination bugs](https://github.com/akkadotnet/akka.net/pull/8150) - Fixes three chained bugs that cause shard unavailability (~6 minutes) during rolling restarts when using distributed lease coordination (e.g. Kubernetes leases):
  * [#8146](https://github.com/akkadotnet/akka.net/issues/8146): The backup `ShardStopped` safety net from #8055 fires spuriously after every successful rebalance, causing the same shard to be allocated to 2+ nodes simultaneously.
  * [#8147](https://github.com/akkadotnet/akka.net/issues/8147): `AwaitingLease` stashes `HandOff` messages indefinitely, preventing the coordinator from reclaiming stuck shards.
  * [#8148](https://github.com/akkadotnet/akka.net/issues/8148): `StartShardRebalanceIfNeeded` silently skips shards during graceful shutdown when a rebalance is already in progress.

**Akka.Core Bug Fixes**

* [Fix Settings.InjectTopLevelFallback race condition bug](https://github.com/akkadotnet/akka.net/pull/8156) - Fixes a race condition in `Settings.InjectTopLevelFallback` that could cause configuration corruption under concurrent access.

**Akka.TestKit Bug Fixes**

* [Fix broken xUnit 3 explicit sender (IAsyncLifetime)](https://github.com/akkadotnet/akka.net/pull/8149) - Fixes broken xUnit 3 explicit sender support when using `IAsyncLifetime`.

1 contributor since release 1.5.64

| COMMITS | LOC+ | LOC- | AUTHOR         |
|---------|------|------|----------------|
| 3       | 263  | 19   | Aaron Stannard |

To see the full set of changes in Akka.NET v1.5.65, [click here](https://github.com/akkadotnet/akka.net/milestone/148?closed=1).

#### 1.5.64 March 31st, 2026 ####

Akka.NET v1.5.64 is a maintenance release focused on completing the xUnit 3 migration for TestKit packages, removing the FluentAssertions transitive dependency, and merging the Multi-Node Test Runner back into the core repository.

**FluentAssertions Removal**

Due to the recent [commercialization of FluentAssertions](https://fluentassertions.com/releases/#800), we have completed the removal of the FluentAssertions transitive dependency from **all** `Akka.TestKit.*` packages. If your tests relied on the transitive FluentAssertions dependency provided by Akka.NET TestKit packages, you will need to add a direct reference to FluentAssertions in your own project.

**TestKit Package Naming Convention**

As part of the ongoing xUnit 3 migration, TestKit packages now follow a naming convention: packages with the `.Xunit` postfix provide xUnit 3 support, while packages with the `.Xunit2` postfix provide xUnit 2 support.

* [Remove FluentAssertions dependency from all TestKit](https://github.com/akkadotnet/akka.net/pull/8130) - Removes the FluentAssertions transitive dependency from all `Akka.TestKit.*` packages.
* [Merge MNTR back to core](https://github.com/akkadotnet/akka.net/pull/8134) - Merges the Akka.NET Multi-Node Test Runner (MNTR) back into the core repository to simplify future Akka.NET development.
* [[xUnit 3] Convert Akka.Cluster.TestKit](https://github.com/akkadotnet/akka.net/pull/8137) - Converts `Akka.Cluster.TestKit` to xUnit 3.
* [[xUnit 3] Convert Akka.MultiNode.TestAdapter](https://github.com/akkadotnet/akka.net/pull/8138) - Converts `Akka.MultiNode.TestAdapter` to xUnit 3.

1 contributor since release 1.5.63

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 5       | 17427| 3874 | Gregorius Soedharmo |

To see the full set of changes in Akka.NET v1.5.64, [click here](https://github.com/akkadotnet/akka.net/milestone/147?closed=1).

#### 1.5.63 March 24th, 2026 ####

Akka.NET v1.5.63 is a maintenance release that includes a **critical Akka.Remote bug fix** along with Akka.Streams fixes and a major migration of all test projects to xUnit 3. 
**All users running Akka.Remote or Akka.Cluster are strongly encouraged to upgrade.**

* [Fix stale ACK causing irrecoverable quarantine after transient network disruption](https://github.com/akkadotnet/akka.net/pull/8116) - Fixes stale ACK could cause an irrecoverable quarantine.
* [Fix race condition in QueueSource offer handling](https://github.com/akkadotnet/akka.net/pull/7875) - Fixes a race condition in `QueueSource`.
* [Fix race condition in UnfoldResourceAsyncSource callback invocation](https://github.com/akkadotnet/akka.net/pull/7859) - Fixes a race condition in `UnfoldResourceAsyncSource`.
* [Remove FluentAssertions from Akka.Persistence.TCK and Akka.Persistence.TCK.Xunit2](https://github.com/akkadotnet/akka.net/pull/8111) - Removes FluentAssertions dependency from the persistence TCK packages.
* [Migrate all test projects to xUnit 3](https://github.com/akkadotnet/akka.net/pull/8060) - Complete migration of all Akka.NET test projects to xUnit 3.

**Important Akka.Remote Bug Fix**

Fixes a critical issue where a stale ACK from a previous connection could cause an irrecoverable quarantine state after a transient network disruption, permanently preventing nodes from re-establishing communication.
In affected scenarios, the only recovery option was a full restart of the quarantined node. This fix ensures that stale ACKs are correctly discarded during reconnection, allowing nodes to recover automatically after network interruptions.

2 contributors since release 1.5.62

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 28      | 4009 | 1637 | Gregorius Soedharmo |
| 2       | 42   | 21   | Aaron Stannard      |

To see the full set of changes in Akka.NET v1.5.63, [click here](https://github.com/akkadotnet/akka.net/milestone/146?closed=1).

#### 1.5.62 March 3rd, 2026 ####

Akka.NET v1.5.62 is a maintenance release with an important bug fix for logging stability when using third-party logging providers.

**Bug Fixes**

* [Fix: catch FormatException in log formatters to prevent third-party logger crashes](https://github.com/akkadotnet/akka.net/pull/8070) - Fixes a crash caused by malformed log format strings.

1 contributor since release 1.5.61

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 1       | 213  | 35   | Aaron Stannard      |

To see the full set of changes in Akka.NET v1.5.62, [click here](https://github.com/akkadotnet/akka.net/milestone/145?closed=1).

#### 1.5.61 February 26th, 2026 ####

Akka.NET v1.5.61 is a maintenance release with important bug fixes for Akka.Cluster.Sharding, Akka.Cluster, and Akka.Core.

**Akka.Cluster.Sharding Bug Fixes**

* [Port Pekko ShardStopped handler + handoff safety net](https://github.com/akkadotnet/akka.net/pull/8055) - Fixes [issue #7500](https://github.com/akkadotnet/akka.net/issues/7500). Resolves a critical issue where shards could fail to hand off indefinitely during scale-up events. 
* [Fix Shard remember-entities flag mismatch causing entity restart failures](https://github.com/akkadotnet/akka.net/pull/8054) - Fixes a bug where the `Entities` class was initialized with the wrong `RememberEntities` flag.
* [Correct self-comparison in ShardCoordinator ResendShardHost handler](https://github.com/akkadotnet/akka.net/pull/8050) - Fixes a bug where the `ResendShardHost` handler compared a region variable to itself instead of comparing against the message's region.

**Akka.Cluster Bug Fixes**

* [VectorClock inequality fixes](https://github.com/akkadotnet/akka.net/pull/8058) - Fixes the `!=` operator which incorrectly delegated to `IsConcurrentWith` instead of being the logical negation of `==`, and fixes the `<` operator to correctly exclude the equal case.
* [Correct format index in 3-arg LogInfo overload](https://github.com/akkadotnet/akka.net/pull/8056) - Fixes a logging bug where the 3-arg `LogInfo` overload used the wrong format index.

**Bug Fixes**

* [Fix wrong randomFactor argument type on RetrySupport.Retry()](https://github.com/akkadotnet/akka.net/pull/8061) - Fixes [issue #8059](https://github.com/akkadotnet/akka.net/issues/8059). Changes the `randomFactor` parameter type from `int` to `double` to match the expected behavior for jitter calculations.
* [AppVersion.CompareTo missing else if breaks comparison symmetry](https://github.com/akkadotnet/akka.net/pull/8051) - Fixes a bug where release versions appeared less than their pre-release counterparts.
* [Remove stray dollar signs from interpolated strings](https://github.com/akkadotnet/akka.net/pull/8057) - Fixes three string interpolation bugs in output in `ClusterHeartbeat`, `ShardRegion`, and `SinkRefImpl`.

**Improvements**

* [Downgrade VirtualPathContainer RemoveChild log from Warning to Debug](https://github.com/akkadotnet/akka.net/pull/8048) - Fixes [issue #8037](https://github.com/akkadotnet/akka.net/issues/8037). Eliminates noisy warnings during high-load `Ask` operations.

4 contributors since release 1.5.60

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 4       | 2085 | 27   | Aaron Stannard      |
| 5       | 52   | 14   | Matt Kotsenas       |
| 3       | 151  | 9    | Gregorius Soedharmo |
| 1       | 2    | 2    | Apoorv Darshan      |

To see the full set of changes in Akka.NET v1.5.61, [click here](https://github.com/akkadotnet/akka.net/milestone/144?closed=1).

#### 1.5.60 February 9th, 2026 ####

Akka.NET v1.5.60 is a maintenance release with a bug fix and a new feature for structured logging.

**Bug Fixes**

* [Fix TestActor initialization race in parallel test startup](https://github.com/akkadotnet/akka.net/pull/8023) - Fixes a race condition where `TestActor` could receive messages before its initialization was complete when tests were run in parallel, causing intermittent test failures.

**New Features**

* [Add logging context enrichment and scopes](https://github.com/akkadotnet/akka.net/pull/8042) - Fixes [issue #7535](https://github.com/akkadotnet/akka.net/issues/7535). Adds `WithContext()` and `BeginScope()` extension methods to `ILoggingAdapter` for structured logging context enrichment. Context properties are automatically included in log output and forwarded to downstream logging providers like Serilog and NLog. [See documentation](https://getakka.net/articles/utilities/logging.html#context-enrichment-and-scopes).

  ```csharp
  var log = Logging.GetLogger(Sys, "example");

  // Enrich a logger with additional structured context
  var enrichedLog = log
      .WithContext("Tenant", "foo")
      .WithContext("Partition", 12);

  enrichedLog.Info("Processing {Offset}", 42);
  // Output: [INFO][...][akka://sys/user/a][Tenant=foo][Partition=12] Processing 42

  // Create a temporary logging scope
  using (var scope = log.BeginScope("RequestId", "REQ-123"))
  {
      scope.Log.Info("Handling request {RequestId}", "REQ-123");
  }
  ```

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 581 | 89 | Aaron Stannard |

To see the full set of changes in Akka.NET v1.5.60, [click here](https://github.com/akkadotnet/akka.net/milestone/143?closed=1).

#### 1.5.59 January 27th, 2026 ####

Akka.NET v1.5.59 is a maintenance release with critical bug fixes and new features for observability.

**Critical Bug Fixes**

* [Fix MergeSeen to filter Seen against current Members](https://github.com/akkadotnet/akka.net/pull/8011) - Fixes [issue #8009](https://github.com/akkadotnet/akka.net/issues/8009). Resolves a cluster gossip serialization failure that could occur with the error "Unknown address in cluster message" when the `Seen` table contained addresses of members that had already left the cluster.

**Bug Fixes**

* [Fix logger initialization continuation race in LoggingBus](https://github.com/akkadotnet/akka.net/pull/8006) - Fixes a race condition during logger initialization that could cause logging failures during actor system startup.
* [Fix Inbox.AwaitResult throwing AggregateException instead of TimeoutException](https://github.com/akkadotnet/akka.net/pull/8005) - `Inbox.AwaitResult` now correctly throws `TimeoutException` when a timeout occurs, rather than wrapping it in an `AggregateException`.
* [Fix DeferAsync async handler nesting bug in CommandAsync](https://github.com/akkadotnet/akka.net/pull/7999) - Fixes [issue #7998](https://github.com/akkadotnet/akka.net/issues/7998). Resolves an issue where `DeferAsync` with an async handler would throw "RunTask calls cannot be nested" when called from `CommandAsync`.
* [Fix AwaitAssertAsync logic causing premature timeout](https://github.com/akkadotnet/akka.net/pull/7986) - Fixes `AwaitAssertAsync` in Akka.TestKit to correctly wait for the full timeout duration before failing assertions.

**New Features**

* [Add ActivityContext capture to LogEvent for trace correlation](https://github.com/akkadotnet/akka.net/pull/7995) - Log events now automatically capture the current `System.Diagnostics.ActivityContext` when created, enabling correlation between Akka.NET logs and distributed traces in observability platforms like OpenTelemetry, Application Insights, and Jaeger.
* [Add BroadcastHub startAfterNrOfConsumers parameter](https://github.com/akkadotnet/akka.net/pull/8018) - Fixes [issue #8017](https://github.com/akkadotnet/akka.net/issues/8017). Port from Apache Pekko - adds a `startAfterNrOfConsumers` parameter to `BroadcastHub.Sink<T>()` that delays broadcasting until the specified number of consumers have subscribed:
  ```csharp
  // Wait for 3 consumers before starting to broadcast
  var sink = BroadcastHub.Sink<int>(startAfterNrOfConsumers: 3, bufferSize: 256);
  ```

**Improvements**

* [CoordinatedShutdown: clearly log the reason why we're exiting](https://github.com/akkadotnet/akka.net/pull/7988) - `CoordinatedShutdown` now logs the specific reason for shutdown at INFO level, making it easier to diagnose why an actor system terminated.

To see the full set of changes in Akka.NET v1.5.59, [click here](https://github.com/akkadotnet/akka.net/milestone/142?closed=1).

#### 1.5.58 January 8th, 2026 ####

Akka.NET v1.5.58 is a maintenance release with important bug fixes and performance improvements.

**.NET 10 Compatibility Fix**

* [Fix .NET 10 CLR shutdown hook breaking change](https://github.com/akkadotnet/akka.net/pull/7964) - Resolves an issue where the CLR shutdown hook behavior changed in .NET 10, ensuring graceful actor system termination works correctly.

**Bug Fixes**

* [Fix TcpListener to not stop accepting connections on transient accept errors](https://github.com/akkadotnet/akka.net/pull/7970) - The `TcpListener` now properly handles transient socket accept errors without stopping to accept further connections.
* [Fix race condition in cluster sharding when entity constructor fails](https://github.com/akkadotnet/akka.net/pull/7981) - Resolves a race condition that could occur when an entity actor's constructor throws an exception.
* [Fix race condition in QueueSink causing async enumerable timeout](https://github.com/akkadotnet/akka.net/pull/7973) - Fixes a race condition in `QueueSink` that could cause timeouts when using async enumerables.
* [Make RemotingTerminator non-FSM to avoid racy FSM log init](https://github.com/akkadotnet/akka.net/pull/7967) - Fixes potential race conditions during actor system shutdown logging.

**Performance Improvements**

* [LogMessage GetProperties without FrozenDictionary](https://github.com/akkadotnet/akka.net/pull/7968) - Improves semantic logging performance by avoiding FrozenDictionary allocations in hot paths.
* [Skip parsing PropertyNames when empty Parameters](https://github.com/akkadotnet/akka.net/pull/7960) - Additional logging optimization that skips unnecessary parsing when log messages have no parameters.

**New Features**

* [Akka.TestKit: configurable expect-no-message-default value](https://github.com/akkadotnet/akka.net/pull/7006) - Fixes [issue #6675](https://github.com/akkadotnet/akka.net/issues/6675). You can now configure the default timeout for `ExpectNoMsg()` via HOCON:
  ```hocon
  akka.test.expect-no-message-default = 100ms
  ```

6 contributors since release 1.5.57

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 7       | 158  | 36   | Aaron Stannard      |
| 2       | 483  | 55   | Gregorius Soedharmo |
| 2       | 11   | 41   | Rolf Kristensen     |
| 1       | 100  | 22   | Yaroslav Paslavskiy |
| 1       | 37   | 53   | Jarkko Pöyry        |
| 1       | 11   | 2    | Petri Kero          |

#### 1.5.57 December 11th, 2025 ####

Akka.NET v1.5.57 is a minor release containing significant new features for Akka.Persistence and structured/semantic logging.

**Akka.Persistence Completion Callbacks and Async Handler Support**

* [Persistence completion callbacks via Defer - simplified alternative](https://github.com/akkadotnet/akka.net/pull/7957) - This release adds completion callback and async handler support to `Persist`, `PersistAsync`, `PersistAll`, and `PersistAllAsync` methods in Akka.Persistence. Key improvements include:
    - **Async Handler Support**: All persist methods now support `Func<TEvent, Task>` handlers for async event processing
    - **Completion Callbacks**: `PersistAll` and `PersistAllAsync` now accept optional completion callbacks (both sync `Action` and async `Func<Task>`) that execute after all events are persisted and handled
    - **Ordering Guarantees**: Completion callbacks use `Defer`/`DeferAsync` internally to maintain strict ordering guarantees
    - **Zero Breaking Changes**: All new APIs are additive overloads

**Persistence Code Examples:**

```csharp
// Async handler support - process events asynchronously
Persist(new OrderPlaced(orderId), async evt =>
{
    await _orderService.ProcessOrderAsync(evt);
});

// PersistAll with completion callback - know when all events are done
PersistAll(orderEvents, evt =>
{
    _state.Apply(evt);
}, onComplete: () =>
{
    // All events persisted and handlers executed
    _logger.Info("Order batch completed");
    Sender.Tell(new BatchComplete());
});

// PersistAll with async handler AND async completion callback
PersistAll(events,
    handler: async evt => await ProcessEventAsync(evt),
    onCompleteAsync: async () =>
    {
        await NotifyCompletionAsync();
        Sender.Tell(Done.Instance);
    });

// PersistAllAsync with completion - allows commands between handlers
PersistAllAsync(largeEventBatch,
    handler: evt => _state.Apply(evt),
    onComplete: () => Sender.Tell(new BatchProcessed()));
```

The implementation maintains Akka.Persistence's strict ordering guarantees by using `Defer`/`DeferAsync` for completion callbacks, ensuring they execute in order even when called with empty event collections. The new async handler invocations (`IAsyncHandlerInvocation`) are processed via `RunTask` to preserve the actor's single-threaded execution model.

**Native Semantic Logging Support**

* [Add native semantic logging support with property extraction](https://github.com/akkadotnet/akka.net/pull/7955) - Fixes [issue #7932](https://github.com/akkadotnet/akka.net/issues/7932). This release adds comprehensive structured logging support to Akka.NET with both positional (`{0}`) and named (`{PropertyName}`) message template parsing, enabling seamless integration with modern logging frameworks like Serilog, NLog, and Microsoft.Extensions.Logging. Key capabilities include:
    - New `LogMessage.PropertyNames` and `GetProperties()` APIs for property extraction
    - `SemanticLogMessageFormatter` as the new default formatter
    - Performance optimized with 75% allocation reduction compared to the previous implementation
    - Zero new dependencies and fully backward compatible
    - EventFilter support for semantic templates in unit tests

1 contributor since release 1.5.56

| COMMITS | LOC+ | LOC- | AUTHOR         |
|---------|------|------|----------------|
| 2       | 3703 | 81   | Aaron Stannard |

To see the full set of changes in Akka.NET v1.5.57, click here:
* [1.5.57-beta1 milestone](https://github.com/akkadotnet/akka.net/milestone/140?closed=1)
* [1.5.57-beta2 milestone](https://github.com/akkadotnet/akka.net/milestone/141?closed=1)

#### 1.5.57-beta2 December 2nd, 2025 ####

Akka.NET v1.5.57-beta2 is a beta release containing significant new APIs for Akka.Persistence that add completion callbacks and async handler support.

**New Features:**

* [Persistence completion callbacks via Defer - simplified alternative](https://github.com/akkadotnet/akka.net/pull/7957) - This release adds completion callback and async handler support to `Persist`, `PersistAsync`, `PersistAll`, and `PersistAllAsync` methods in Akka.Persistence. Key improvements include:
  - **Async Handler Support**: All persist methods now support `Func<TEvent, Task>` handlers for async event processing
  - **Completion Callbacks**: `PersistAll` and `PersistAllAsync` now accept optional completion callbacks (both sync `Action` and async `Func<Task>`) that execute after all events are persisted and handled
  - **Ordering Guarantees**: Completion callbacks use `Defer`/`DeferAsync` internally to maintain strict ordering guarantees
  - **Zero Breaking Changes**: All new APIs are additive overloads

**Code Examples:**

```csharp
// Async handler support - process events asynchronously
Persist(new OrderPlaced(orderId), async evt =>
{
    await _orderService.ProcessOrderAsync(evt);
});

// PersistAll with completion callback - know when all events are done
PersistAll(orderEvents, evt =>
{
    _state.Apply(evt);
}, onComplete: () =>
{
    // All events persisted and handlers executed
    _logger.Info("Order batch completed");
    Sender.Tell(new BatchComplete());
});

// PersistAll with async handler AND async completion callback
PersistAll(events,
    handler: async evt => await ProcessEventAsync(evt),
    onCompleteAsync: async () =>
    {
        await NotifyCompletionAsync();
        Sender.Tell(Done.Instance);
    });

// PersistAllAsync with completion - allows commands between handlers
PersistAllAsync(largeEventBatch,
    handler: evt => _state.Apply(evt),
    onComplete: () => Sender.Tell(new BatchProcessed()));
```

**Technical Details:**

The implementation maintains Akka.Persistence's strict ordering guarantees by using `Defer`/`DeferAsync` for completion callbacks, ensuring they execute in order even when called with empty event collections. The new async handler invocations (`IAsyncHandlerInvocation`) are processed via `RunTask` to preserve the actor's single-threaded execution model.

1 contributor since release 1.5.57-beta1

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 1386 | 67 | Aaron Stannard |

To [see the full set of changes in Akka.NET v1.5.57-beta2, click here](https://github.com/akkadotnet/akka.net/milestone/141?closed=1)

#### 1.5.57-beta1 December 2nd, 2025 ####

Akka.NET v1.5.57-beta1 is a beta release containing a significant new feature for structured/semantic logging.

**New Features:**

* [Add native semantic logging support with property extraction](https://github.com/akkadotnet/akka.net/pull/7955) - Fixes [issue #7932](https://github.com/akkadotnet/akka.net/issues/7932). This release adds comprehensive structured logging support to Akka.NET with both positional (`{0}`) and named (`{PropertyName}`) message template parsing, enabling seamless integration with modern logging frameworks like Serilog, NLog, and Microsoft.Extensions.Logging. Key capabilities include:
  - New `LogMessage.PropertyNames` and `GetProperties()` APIs for property extraction
  - `SemanticLogMessageFormatter` as the new default formatter
  - Performance optimized with 75% allocation reduction compared to the previous implementation
  - Zero new dependencies and fully backward compatible
  - EventFilter support for semantic templates in unit tests

1 contributor since release 1.5.56

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 2317 | 14 | Aaron Stannard |

To [see the full set of changes in Akka.NET v1.5.57-beta1, click here](https://github.com/akkadotnet/akka.net/milestone/140?closed=1)

#### 1.5.56 November 25th, 2025 ####

Akka.NET v1.5.56 is a patch release containing important bug fixes for Akka.Remote and Akka.Streams.

**Bug Fixes:**

* [Fix: Akka.Remote should not shutdown on invalid TLS traffic](https://github.com/akkadotnet/akka.net/pull/7952) - Fixes [issue #7938](https://github.com/akkadotnet/akka.net/issues/7938) where invalid traffic (like HTTP requests) hitting a TLS-enabled Akka.Remote port would cause the entire ActorSystem to shut down. Server now rejects invalid connections gracefully without terminating.

* [fix(streams): prevent race condition in ChannelSource on channel completion](https://github.com/akkadotnet/akka.net/pull/7951) - Fixes [issue #7940](https://github.com/akkadotnet/akka.net/issues/7940) where a `NullReferenceException` could occur when completing a `ChannelWriter` while the stream is waiting for data. Added atomic flag to prevent race condition between `OnReaderComplete` and `OnValueRead` callbacks.

1 contributor since release 1.5.55

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 162 | 6 | Aaron Stannard |

To [see the full set of changes in Akka.NET v1.5.56, click here](https://github.com/akkadotnet/akka.net/milestone/139?closed=1)

#### 1.5.55 October 26th, 2025 ####

Akka.NET v1.5.55 is a patch release containing important stability and security improvements for Akka.Remote.

**Akka.Remote Stability Improvements:**

* [Akka.Remote: harden EndpointWriter against serialization failures](https://github.com/akkadotnet/akka.net/pull/7925) - Fixes [issue #7922](https://github.com/akkadotnet/akka.net/issues/7922) by hardening the `EndpointWriter` against a broader range of potential serialization failures, improving overall remoting stability.

**Akka.Remote Security Improvements:**

* [Custom certificate validation with single execution path - fixes mTLS asymmetry bug](https://github.com/akkadotnet/akka.net/pull/7921) - Fixes [issue #7914](https://github.com/akkadotnet/akka.net/issues/7914) by introducing programmatic certificate validation helpers through the new `CertificateValidation` factory class. This release adds 7 new validation helper methods including `ValidateChain()`, `ValidateHostname()`, `PinnedCertificate()`, `ValidateSubject()`, `ValidateIssuer()`, `Combine()`, and `ChainPlusThen()`. The update also fixes an mTLS asymmetry bug where server-side hostname validation was not being applied consistently with client-side validation, all while maintaining full backward compatibility with existing HOCON-based validation.

* [Fix DotNettySslSetup being ignored when HOCON has valid SSL config](https://github.com/akkadotnet/akka.net/pull/7919) - Fixes [issue #7917](https://github.com/akkadotnet/akka.net/issues/7917) where programmatic `DotNettySslSetup` settings were incorrectly being overridden by HOCON configuration. Programmatic configuration now correctly takes precedence over HOCON defaults as intended.

1 contributor since release 1.5.54

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 1605 | 289 | Aaron Stannard |


To [see the full set of changes in Akka.NET v1.5.55, click here](https://github.com/akkadotnet/akka.net/milestone/138?closed=1)

#### 1.5.54 October 17th, 2025 ####

Akka.NET v1.5.54 is a patch release containing important bug fixes for Akka.Streams and Akka.DistributedData.

**Bug Fixes:**

* [Fix SourceRef.Source and SinkRef.Sink non-idempotent property bug](https://github.com/akkadotnet/akka.net/pull/7907) - Fixes [issue #7895](https://github.com/akkadotnet/akka.net/issues/7895) where `ISourceRef<T>.Source` and `ISinkRef<T>.Sink` properties created new stage instances on every access, causing race conditions and intermittent subscription timeouts. These properties are now idempotent using `Lazy<T>`, preventing failures from accidental property access (debugger inspection, logging, serialization frameworks).

* [Fix LWWDictionary.Delta ArgumentNullException when underlying delta is null](https://github.com/akkadotnet/akka.net/pull/7912) - Fixes [issue #7910](https://github.com/akkadotnet/akka.net/issues/7910) where `LWWDictionary.Delta` would throw `ArgumentNullException` when the underlying `ORDictionary.Delta` was `null`, which is a legitimate state after initialization or calling `ResetDelta()`.

1 contributor since release 1.5.53

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 159 | 20 | Aaron Stannard |


To [see the full set of changes in Akka.NET v1.5.54, click here](https://github.com/akkadotnet/akka.net/milestone/137?closed=1)

#### 1.5.53 October 9th, 2025 ####

Akka.NET v1.5.53 is a security patch containing important fixes for TLS/SSL hostname validation and improved error diagnostics for certificate authentication issues.

**Security Fixes:**

* [Fix TLS hostname validation bug and add configurable validation](https://github.com/akkadotnet/akka.net/pull/7897) - Fixes a critical bug where TLS clients validated against their own certificate DNS name instead of the remote server address, particularly affecting mutual TLS scenarios. This release also adds a new `validate-certificate-hostname` configuration option to `akka.remote.dot-netty.tcp` (defaults to `false` for backward compatibility) and introduces type-safe validation APIs through the new `TlsValidationCallbacks` factory class.

**Improvements:**

* [Improve TLS/SSL certificate error messages during handshake failures](https://github.com/akkadotnet/akka.net/pull/7891) - Provides human-readable, actionable error messages for TLS/SSL certificate validation failures with detailed troubleshooting guidance, significantly improving the developer experience when configuring certificate-based authentication.

1 contributor since release 1.5.52

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 1060 | 77 | Aaron Stannard |


To [see the full set of changes in Akka.NET v1.5.53, click here](https://github.com/akkadotnet/akka.net/milestone/136?closed=1)

#### 1.5.52 October 6th, 2025 ####

**SECURITY PATCH**

Akka.NET v1.5.52 is a security patch containing crucial fixes for enforcing certificate-based authentication using mTLS enforcement. Please see https://getakka.net/articles/remoting/security.html for details on how this works.

* [Akka.Remote: implement mutual TLS authentication support](https://github.com/akkadotnet/akka.net/pull/7851)
* [Akka.Remote: validate SSL certificate private key access at server startup](https://github.com/akkadotnet/akka.net/pull/7847)

Other fixes:

* [Akka.Cluster.Sharding: ShardedDaemonSets: randomize starting worker index](https://github.com/akkadotnet/akka.net/pull/7857)

1 contributors since release 1.5.51

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 1193 | 149 | Aaron Stannard |


To [see the full set of changes in Akka.NET v1.5.52, click here](https://github.com/akkadotnet/akka.net/milestone/135?closed=1)

#### 1.5.51 October 1st, 2025 ####

Akka.NET v1.5.51 is a minor patch containing a remoting bug fix and add required codes to support persistence health check.

* [Remote: Fix DotNetty TLS handshake error handling](https://github.com/akkadotnet/akka.net/pull/7839)
* [Persistence: Add health check handling code](https://github.com/akkadotnet/akka.net/pull/7842)

2 contributors since release 1.5.50

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 1       | 609  | 31   | Aaron Stannard      |
| 1       | 139  | 5    | Gregorius Soedharmo |

To [see the full set of changes in Akka.NET v1.5.51, click here](https://github.com/akkadotnet/akka.net/milestone/134?closed=1)

#### 1.5.50 September 22nd, 2025 ####

Akka.NET v1.5.50 is a minor patch containing a bug fix.

* [Remote: Propagate error from DotNetty TLS Handshake failure to Akka.Remote](https://github.com/akkadotnet/akka.net/pull/7824)

1 contributor since release 1.5.49

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 1       | 187  | 1    | Gregorius Soedharmo |

To [see the full set of changes in Akka.NET v1.5.50, click here](https://github.com/akkadotnet/akka.net/milestone/133?closed=1)

#### 1.5.49 September 10th, 2025 ####

Akka.NET v1.5.49 is a minor patch containing several bug fixes.

* [Core: Fix IIS/Windows Service console race condition](https://github.com/akkadotnet/akka.net/pull/7793)
* [DData: Fix Replicator.ReceiveUnsubscribe boolean logic](https://github.com/akkadotnet/akka.net/pull/7809)
* [Streams: Fix ConcurrentAsyncCallback with ChannelSource throws NRE](https://github.com/akkadotnet/akka.net/pull/7808)

3 contributors since release 1.5.48

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 18      | 6011 | 9343 | Aaron Stannard      |
| 18      | 3760 | 3880 | Gregorius Soedharmo |
| 1       | 1    | 1    | dependabot[bot]     |

To [see the full set of changes in Akka.NET v1.5.49, click here](https://github.com/akkadotnet/akka.net/milestone/132?closed=1)

#### 1.5.48 August 21st, 2025 ####

Akka.NET v1.5.48 is a minor patch containing stability improvement to Akka.TestKit.

* [TestKit: Fix deadlock during parallel test execution](https://github.com/akkadotnet/akka.net/pull/7787)

2 contributors since release 1.5.47

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 4       | 5494 | 5561 | Aaron Stannard      |
| 2       | 204  | 66   | Gregorius Soedharmo |

To [see the full set of changes in Akka.NET v1.5.48, click here](https://github.com/akkadotnet/akka.net/milestone/131?closed=1)

* Core: Add `ILoggingAdapter` context enrichment, explicit scopes, and bracketed context output in StandardOutLogger and Xunit logger
* Akka.Streams: Add cancellation-aware `Source.Queue` offers so backpressured pending offers can be canceled without later emitting the canceled element.
* Build: Bump `MessagePack` to 3.1.7 to address [CVE-2026-48109](https://github.com/advisories/GHSA-hv8m-jj95-wg3x) (LZ4 decompression out-of-bounds read)

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
