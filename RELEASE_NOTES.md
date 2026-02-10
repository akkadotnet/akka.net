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
