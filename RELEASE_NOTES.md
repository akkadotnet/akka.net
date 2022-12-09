#### 1.4.47 December 9th 2022 ####
Akka.NET v1.4.47 is a maintenance patch for Akka.NET v1.4.46 that includes a variety of bug fixes, performance improvements, and new features.

**Actor Telemetry**

Starting in Akka.NET v1.4.47 local and remotely deployed actors will now emit events when being started, stopped, and restarted:

```csharp
public interface IActorTelemetryEvent : INoSerializationVerificationNeeded, INotInfluenceReceiveTimeout
{
    /// <summary>
    /// The actor who emitted this event.
    /// </summary>
    IActorRef Subject {get;}
    
    /// <summary>
    /// The implementation type for this actor.
    /// </summary>
    Type ActorType { get; }
}

/// <summary>
/// Event emitted when actor starts.
/// </summary>
public sealed class ActorStarted : IActorTelemetryEvent
{
    public IActorRef Subject { get; }
    public Type ActorType { get; }
}

/// <summary>
/// Event emitted when actor shuts down.
/// </summary>
public sealed class ActorStopped : IActorTelemetryEvent
{
    public IActorRef Subject { get; }
    public Type ActorType { get; }
}

/// <summary>
/// Emitted when an actor restarts.
/// </summary>
public sealed class ActorRestarted : IActorTelemetryEvent
{
    public IActorRef Subject { get; }
    public Type ActorType { get; }
    
    public Exception Reason { get; }
}
```

These events will be consumed from popular Akka.NET observability and management tools such as [Phobos](https://phobos.petabridge.com/) and [Petabridge.Cmd](https://cmd.petabridge.com/) to help provide users with more accurate insights into actor workloads over time, but you can also consume these events yourself by subscribing to them via the `EventStream`:

```csharp
// subscribe to all actor telemetry events
Context.System.EventStream.Subscribe(Self, typeof(IActorTelemetryEvent));
```

**By default actor telemetry is disabled** - to enable it you'll need to turn it on via the following HOCON setting:

```hocon
akka.actor.telemetry.enabled = on
```

The performance impact of enabling telemetry is negligible, as you can [see via our benchmarks](https://github.com/akkadotnet/akka.net/pull/6294#issuecomment-1340251897).

**Fixes and Updates**

* [Akka.Streams: Fixed `System.NotSupportedException` when disposing stage with materialized `IAsyncEnumerable`](https://github.com/akkadotnet/akka.net/issues/6280)
* [Akka.Streams: `ReuseLatest` stage to repeatedly emit the most recent value until a newer one is pushed](https://github.com/akkadotnet/akka.net/pull/6262)
* [Akka.Remote: eliminate `ActorPath.ToSerializationFormat` UID allocations](https://github.com/akkadotnet/akka.net/pull/6195) - should provide a noticeable Akka.Remote performance improvement.
* [Akka.Remote: Remoting and an exception as a payload message ](https://github.com/akkadotnet/akka.net/issues/3903) - `Exception` types are now serialized properly inside `Status.Failure` messages over the wire. `Status.Failure` and `Status.Success` messages are now managed by Protobuf - so you might see some deserialization errors while upgrading if those types are being exchanged over the wire.
* [Akka.TestKit: `TestActorRef` can not catch exceptions on asynchronous methods](https://github.com/akkadotnet/akka.net/issues/6265)

You can see the [full set of tracked issues for Akka.NET v1.4.47 here](https://github.com/akkadotnet/akka.net/issues?q=is%3Aclosed+milestone%3A1.4.47).

#### 1.4.46 November 15th 2022 ####
Akka.NET v1.4.46 is a security patch for Akka.NET v1.4.45 but also includes some other fixes.

**Security Advisory**: Akka.NET v1.4.45 and earlier depend on an old System.Configuration.ConfigurationManager version 4.7.0 which transitively depends on System.Common.Drawing v4.7.0. The System.Common.Drawing v4.7.0 is affected by a remote code execution vulnerability [GHSA-ghhp-997w-qr28](https://github.com/advisories/GHSA-ghhp-997w-qr28).

We have separately created a security advisory for [Akka.NET Versions < 1.4.46 and < 1.5.0-alpha3 to track this issue](https://github.com/akkadotnet/akka.net/security/advisories/GHSA-gpv5-rp6w-58r8).

**Fixes and Updates**

* [Akka: Upgrade to Newtonsoft.Json 13.0.1 as minimum version](https://github.com/akkadotnet/akka.net/pull/6252)
* [Akka: Upgrade to System.Configuration.ConfigurationManager 6.0.1](https://github.com/akkadotnet/akka.net/pull/6253) - resolves security issue.
* [Akka.IO: Report cause for Akka/IO TCP `CommandFailed` events](https://github.com/akkadotnet/akka.net/pull/6224)
* [Akka.Cluster.Tools: Make sure that `DeadLetter`s published by `DistributedPubSubMediator` contain full context of topic](https://github.com/akkadotnet/akka.net/pull/6209)
* [Akka.Cluster.Metrics: Improve CPU/Memory metrics collection at Akka.Cluster.Metrics](https://github.com/akkadotnet/akka.net/issues/4142) - built-in metrics are now much more accurate.

You can see the [full set of tracked issues for Akka.NET v1.4.46 here](https://github.com/akkadotnet/akka.net/milestone/77).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 10 | 2027 | 188 | Aaron Stannard |
| 1 | 157 | 10 | Gregorius Soedharmo |


#### 1.4.45 October 19th 2022 ####
Akka.NET v1.4.45 is a patch release for Akka.NET v1.4 for a bug introduced in v1.4.44.

**Patch**
* [Akka.NET: Revert change to ConfigurationException that is causing binary backward compatibility problem](https://github.com/akkadotnet/akka.net/pull/6201)

#### 1.4.44 October 17th 2022 ####
Akka.NET v1.4.44 is a maintenance release for Akka.NET v1.4 that contains numerous performance improvements in critical areas, including core actor message processing and Akka.Remote.

**Performance Fixes**

* [remove delegate allocation from `ForkJoinDispatcher` and `DedicatedThreadPool`](https://github.com/akkadotnet/akka.net/pull/6143)
* [eliminate `Mailbox` delegate allocations](https://github.com/akkadotnet/akka.net/pull/6134)
* [Reduce `FSM<TState, TData>` allocations](https://github.com/akkadotnet/akka.net/pull/6145)
* [removed boxing allocations inside `FSM.State.Equals`](https://github.com/akkadotnet/akka.net/pull/6183)
* [Eliminate `DefaultLogMessageFormatter` allocations](https://github.com/akkadotnet/akka.net/pull/6168)

In sum you should expect to see total memory consumption, garbage collection, and throughput improve when you upgrade to Akka.NET v1.4.44.

**Other Features and Improvements**

* [Akka.Cluster and Akka.Cluster.Sharding: should throw human-friendly exception when accessing cluster / sharding plugins when clustering is not running](https://github.com/akkadotnet/akka.net/issues/6163)
* [Akka.Cluster.Sharding: Add `HashCodeMessageExtractor` factory](https://github.com/akkadotnet/akka.net/pull/6182)
* [Akka.Persistence.Sql.Common: Fix `DbCommand.CommandTimeout` in `BatchingSqlJournal`](https://github.com/akkadotnet/akka.net/pull/6180)

You can [see the full list of fixes in Akka.NET v1.4.44 here](https://github.com/akkadotnet/akka.net/milestone/75).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 10 | 651 | 69 | @Aaronontheweb |
| 4 | 275 | 17 | @Arkatufus |

#### 1.4.42 September 23 2022 ####
Akka.NET v1.4.42 is a minor release that contains some minor bug fixes.

* [DData: Suppress gossip message from showing up in debug log unless verbose debug logging is turned on](https://github.com/akkadotnet/akka.net/issues/6091)
* [TestKit: TestKit automatically injects the default TestKit default configuration if an ActorSystem is passed into its constructor](https://github.com/akkadotnet/akka.net/issues/6094)
* [Sharding: Added a new `GetEntityLocation` query message to retrieve an entity address location in the shard region](https://github.com/akkadotnet/akka.net/issues/6101)
  
  In order to use this query, "remember entities" should be turned on _or_ the provided shard `IMessageExtractor` supports the `ShardRegion.StartEntity` message. Complete documentation can be read [here](https://getakka.net/articles/clustering/cluster-sharding.html#querying-for-the-location-of-specific-entities)

If you want to see the [full set of changes made in Akka.NET v1.4.42, click here](https://github.com/akkadotnet/akka.net/milestone/73).

| COMMITS | LOC+ | LOC- | AUTHOR              |
|---------|------|------|---------------------|
| 3       | 66   | 3    | Gregorius Soedharmo |
| 1       | 557  | 118  | Aaron Stannard      |

#### 1.4.41 August 31 2022 ####
Akka.NET v1.4.41 is a minor release that contains some minor bug fix and throughput performance improvement for Akka.Remote

* [Akka: Fix AddLogger in LoggingBus](https://github.com/akkadotnet/akka.net/issues/6028)
 
  Akka loggers are now loaded asynchronously by default. The `ActorSystem` will wait at most `akka.logger-startup-timeout` period long (5 seconds by default) for all loggers to report that they are ready before continuing the start-up process. 

  A warning will be logged on each loggers that did not report within this grace period. These loggers will still be awaited upon inside a detached Task until either it is ready or the `ActorSystem` is shut down. 
 
  These late loggers will not capture all log events until they are ready. If your logs are missing portion of the start-up events, check that the logger were loaded within this grace period.

* [Akka: Log Exception cause inside Directive.Resume SupervisorStrategy warning log](https://github.com/akkadotnet/akka.net/issues/6070)
* [DData: Add "verbose-debug-logging" setting to suppress debug message spam](https://github.com/akkadotnet/akka.net/issues/6080)
* [Akka: Regenerate protobuf codes](https://github.com/akkadotnet/akka.net/issues/6087)

  All protobuf codes were re-generated, causing a significant improvement in message deserialization, increasing `Akka.Remote` throughput.

__Before__
``` ini
BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19041.1415 (2004/May2020Update/20H1)
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK=6.0.200
  [Host]     : .NET 6.0.2 (6.0.222.6406), X64 RyuJIT
  DefaultJob : .NET 6.0.2 (6.0.222.6406), X64 RyuJIT
```
|                 Method |       Mean |    Error |   StdDev |  Gen 0 |  Gen 1 | Allocated |
|----------------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
|        WritePayloadPdu | 1,669.6 ns | 21.10 ns | 19.74 ns | 0.2156 |      - |   1,808 B |
|       DecodePayloadPdu | 2,039.7 ns | 12.52 ns | 11.71 ns | 0.2156 | 0.0031 |   1,816 B |
|          DecodePduOnly |   131.3 ns |  1.32 ns |  1.11 ns | 0.0563 | 0.0002 |     472 B |
|      DecodeMessageOnly | 1,665.0 ns | 15.03 ns | 14.05 ns | 0.1406 |      - |   1,184 B |
| DeserializePayloadOnly |   151.2 ns |  1.88 ns |  1.76 ns | 0.0199 |      - |     168 B |

__After__
``` ini
BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19041.1415 (2004/May2020Update/20H1)
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK=6.0.200
  [Host]     : .NET 6.0.2 (6.0.222.6406), X64 RyuJIT
  DefaultJob : .NET 6.0.2 (6.0.222.6406), X64 RyuJIT
```
|                 Method |       Mean |    Error |   StdDev |  Gen 0 |  Gen 1 | Allocated |
|----------------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
|        WritePayloadPdu | 1,623.4 ns | 19.95 ns | 18.66 ns | 0.2219 | 0.0031 |   1,880 B |
|       DecodePayloadPdu | 1,738.6 ns | 22.79 ns | 21.31 ns | 0.2250 |      - |   1,888 B |
|          DecodePduOnly |   175.1 ns |  2.31 ns |  1.93 ns | 0.0572 |      - |     480 B |
|      DecodeMessageOnly | 1,296.8 ns | 11.89 ns | 10.54 ns | 0.1469 | 0.0016 |   1,232 B |
| DeserializePayloadOnly |   143.6 ns |  1.59 ns |  1.33 ns | 0.0199 | 0.0002 |     168 B |
 

If you want to see the [full set of changes made in Akka.NET v1.4.41, click here](https://github.com/akkadotnet/akka.net/milestone/72).

| COMMITS | LOC+  | LOC- | AUTHOR              |
|---------|-------|------|---------------------|
| 4       | 13003 | 1150 | Gregorius Soedharmo |
| 1       | 3     | 4    | Aaron Stannard      |

#### 1.4.40 July 19 2022 ####
Akka.NET v1.4.40 is a minor release that contains a bug fix for DotNetty SSL support.

* [Akka.Remote: SSL Configuration Fails even EnbleSsl property is set to false](https://github.com/akkadotnet/akka.net/issues/6043)
* [Akka.Streams: Add IAsyncEnumerable Source](https://github.com/akkadotnet/akka.net/issues/6047)

If you want to see the [full set of changes made in Akka.NET v1.4.40, click here](https://github.com/akkadotnet/akka.net/milestone/71).

| COMMITS | LOC+  | LOC- | AUTHOR              |
|---------|-------|------|---------------------|
| 8       | 544   | 64   | Gregorius Soedharmo |
| 1       | 669   | 3    | Aaron Stannard      |
| 1       | 123   | 26   | Ebere Abanonu       |
| 1       | 101   | 3    | aminchenkov         |

#### 1.4.39 June 1 2022 ####
Akka.NET v1.4.39 is a minor release that contains some very important bug fixes for Akka.Remote and Akka.Cluster users.

* [Akka.Cluster: Error in `SplitBrainResolver.PreStart` when using `ChannelTaskScheduler` for internal-dispatcher](https://github.com/akkadotnet/akka.net/issues/5962)
* [Akka.Cluster.Sharding: make PersistentShardCoordinator a tolerant reader](https://github.com/akkadotnet/akka.net/issues/5604) - Akka.Persistence-backed sharding is more lenient when recovering state.
* [Akka.Remote: Trap all `Exception`s thrown while trying to dispatch messages in `Akka.Remote.EndpointReader`](https://github.com/akkadotnet/akka.net/pull/5971) - any kind of exception thrown during deserialization can no longer force a disassociation to occur in Akka.Remote.

If you want to see the [full set of changes made in Akka.NET v1.4.39, click here](https://github.com/akkadotnet/akka.net/milestone/70).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 204 | 99 | Aaron Stannard |
| 1 | 1 | 13 | Gregorius Soedharmo |

#### 1.4.38 May 6 2022 ####
Akka.NET v1.4.38 is a minor release that contains some minor bug fixes.

* [Streams: Add `allowClosedSubstreamRecreation` option to GroupBy](https://github.com/akkadotnet/akka.net/pull/5882)
* [Streams: Fix `Source.ActorRef` not completing bug](https://github.com/akkadotnet/akka.net/pull/5883)
* [Remote: Fix typo thumbprint in `akka.remote` HOCON configuration](https://github.com/akkadotnet/akka.net/pull/5903)
* [Cluster: Fix `ChannelTaskScheduler` to work inside `Akka.Cluster`](https://github.com/akkadotnet/akka.net/pull/5920)

If you want to see the [full set of changes made in Akka.NET v1.4.38, click here](https://github.com/akkadotnet/akka.net/milestone/69?closed=1).

| COMMITS | LOC+  | LOC- | AUTHOR              |
|---------|-------|------|---------------------|
| 6       | 177   | 93   | Gregorius Soedharmo |
| 5       | 424   | 156  | Ismael Hamed        |
| 2       | 86    | 89   | Aaron Stannard      |
| 1       | 45    | 209  | Simon Cropp         |
| 1       | 1     | 1    | dependabot[bot]     |

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
