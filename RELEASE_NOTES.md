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
