#### 1.6.0 August 15th, 2025 ####

**Placeholder for nightly build**

* Core: Add `ILoggingAdapter` context enrichment, explicit scopes, and bracketed context output in StandardOutLogger and Xunit logger
* Akka.Streams: Add cancellation-aware `Source.Queue` offers so backpressured pending offers can be canceled without later emitting the canceled element.
* Akka.Streams: Fixed `Source.From(IAsyncEnumerable<T>)` cleanup so cancellation waits for any in-flight `MoveNextAsync()` before disposing the async enumerator and its cancellation token source.
* Build: Bump `MessagePack` to 3.1.7 to address [CVE-2026-48109](https://github.com/advisories/GHSA-hv8m-jj95-wg3x) (LZ4 decompression out-of-bounds read)
* [Core: Fix consistent-hashing router could wedge cluster-wide after a 32-bit hash collision](https://github.com/akkadotnet/akka.net/issues/8031) - Fixes [#8031](https://github.com/akkadotnet/akka.net/issues/8031) (forward-port of [#8294](https://github.com/akkadotnet/akka.net/pull/8294)): When two virtual nodes collided in the 32-bit consistent-hash ring (increasingly likely at high routee counts, e.g. when the ring was rebuilt after a node was downed), `ConsistentHash.Create` threw `"An entry with the same key already exists"`. The consistent-hashing router swallowed the exception and returned `NoRoutee` for **every** subsequent message until a manual restart. The ring now re-hashes and linear-probes to the next free slot on a collision instead of throwing. This keeps the hash distribution unchanged and produces a byte-identical ring to prior versions whenever no collision occurs (safe for rolling upgrades), and also protects `Akka.Cluster.Tools`' `ClusterReceptionist`, which builds the same ring.

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
