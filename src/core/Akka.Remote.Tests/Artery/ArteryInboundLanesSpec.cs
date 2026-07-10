//-----------------------------------------------------------------------
// <copyright file="ArteryInboundLanesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Remote.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// End-to-end tests for inbound lanes (<c>akka.remote.artery.advanced.inbound-lanes</c> &gt; 1):
    /// <see cref="ArteryInboundProcessingStage"/>'s per-connection fan-out of ordinary-stream
    /// messages across N bounded-channel-fed consumer loops. Every test here goes through the REAL
    /// <see cref="ArteryRemoting"/> TCP transport (never the low-level GraphStage
    /// TestSource/TestSink harness <c>ArteryHandshakeSpec</c> uses) -- lanes only exist inside
    /// <c>ArteryRemoting.HandleIncomingConnection</c>'s real per-connection composition, and
    /// <see cref="InboundHandshakeStage"/>/<see cref="SystemMessageAckerStage"/> themselves are
    /// completely unmodified by this feature (see <see cref="ArteryInboundProcessingStage"/>'s
    /// type-level remarks for why), so re-running THEIR isolated unit tests under a lanes=4 config
    /// variant would exercise nothing new; a real handshake-then-dispatch round trip does.
    ///
    /// <para>
    /// <b>Maintainer policy, honored throughout this file:</b> no wall-clock thresholds. Every
    /// assertion is progress/order/completion/bounded-state, or a liveness await ("this must
    /// eventually happen"), never a measurement of how long something took.
    /// </para>
    /// </summary>
    public class ArteryInboundLanesSpec : AkkaSpec
    {
        public ArteryInboundLanesSpec(ITestOutputHelper output) : base(output)
        {
        }

        // Generous outbound-message-queue-size headroom (well above the production default of
        // 3072): several tests below fire a concurrent multi-recipient burst through ONE
        // association's SINGLE shared outbound queue purely to prove per-recipient ORDER on the
        // receiving (lanes) side -- an incidental overflow drop on the SENDING side would fail
        // those tests for a reason that has nothing to do with inbound lanes.
        private static Config ArteryConfig(int inboundLanes = 1) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{inboundLanes}}
            akka.remote.artery.advanced.outbound-message-queue-size = 20000
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string RemotePath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        /// <summary>
        /// <see cref="ArteryConfig"/> plus <see cref="GateBlockingSerializer"/> wired in via
        /// <c>serialization-bindings</c> (same idiom every other custom-serializer spec in this
        /// repo uses -- see e.g. <c>AbstractTransientSerializationErrorSpec</c> -- rather than any
        /// lanes-specific mechanism) for the backpressure-parking spec below. Applied to BOTH ends
        /// of the association: the SENDER needs the binding to encode a <see cref="GatedInt"/> at
        /// all (<c>Serialization.FindSerializerFor</c> throws otherwise), even though only the
        /// RECEIVING side's <see cref="GateBlockingSerializer.FromBinary"/> call ever actually
        /// blocks.
        /// </summary>
        private static Config GatedConfig(int inboundLanes, int inboundLaneBufferSize) => ConfigurationFactory.ParseString($$"""
            akka.remote.artery.advanced.inbound-lane-buffer-size = {{inboundLaneBufferSize}}
            akka.actor.serializers.gate-blocking = "Akka.Remote.Tests.Artery.ArteryInboundLanesSpec+GateBlockingSerializer, Akka.Remote.Tests"
            akka.actor.serialization-bindings {
                "Akka.Remote.Tests.Artery.ArteryInboundLanesSpec+GatedInt, Akka.Remote.Tests" = gate-blocking
            }
            """).WithFallback(ArteryConfig(inboundLanes));

        /// <summary>
        /// Payload for the backpressure-parking spec below. Carries the per-test gate key and the
        /// recorded value entirely through the wire MANIFEST (<see cref="GateBlockingSerializer.ToBinary"/>
        /// itself is trivial) -- there is nothing to block on the SENDING side, only
        /// <see cref="GateBlockingSerializer.FromBinary"/>, on the receiving lane consumer.
        /// </summary>
        private sealed class GatedInt
        {
            public GatedInt(string gateKey, int value)
            {
                GateKey = gateKey;
                Value = value;
            }

            public string GateKey { get; }
            public int Value { get; }
        }

        /// <summary>
        /// Test-only serializer (registered via HOCON <c>serialization-bindings</c> -- see
        /// <see cref="GatedConfig"/> -- same idiom as <c>AbstractTransientSerializationErrorSpec.TestSerializer</c>)
        /// whose <see cref="FromBinary"/> blocks on a per-<paramref name="gateKey"/> gate until
        /// <see cref="OpenGate"/> is called. This is the ONLY way this port can deterministically
        /// stall an inbound lane's consumer loop: a slow RECIPIENT cannot do it, because the lane's
        /// final dispatch is a fire-and-forget <c>Tell</c> into an unbounded mailbox (see
        /// <c>ArteryInboundProcessingStage.RunLaneConsumer</c>'s remarks) -- deserialization is the
        /// only synchronous, blockable step in a lane consumer's loop.
        /// </summary>
        private sealed class GateBlockingSerializer : SerializerWithStringManifest
        {
            // Keyed by an arbitrary per-test gate key (a fresh Guid per test method) rather than
            // one shared field, so a stray un-opened gate from one run/spec can never bleed into a
            // different one's key.
            private static readonly ConcurrentDictionary<string, ManualResetEventSlim> Gates = new();

            // Observability companion to Gates: lets the test confirm (via AwaitAssertAsync -- a
            // progress wait, not a wall-clock one) that the FIRST gated message has actually
            // reached FromBinary and is blocked, before it goes on to release the gate.
            private static readonly ConcurrentDictionary<string, bool> Entered = new();

            public GateBlockingSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            // Arbitrary, hardcoded (never auto-assigned) so both ends of the wire agree on it
            // regardless of HOCON parse order -- mirrors AbstractTransientSerializationErrorSpec.TestSerializer's
            // Identifier=666 idiom (a different number purely so the two can never collide if ever
            // loaded together).
            public override int Identifier => 918273;

            public override string Manifest(object o)
            {
                var gated = (GatedInt)o;
                return $"{gated.GateKey}|{gated.Value}";
            }

            public override byte[] ToBinary(object obj) => Array.Empty<byte>();

            public override object FromBinary(byte[] bytes, string manifest)
            {
                var separator = manifest.IndexOf('|');
                var gateKey = manifest.Substring(0, separator);
                var value = int.Parse(manifest.Substring(separator + 1));

                Entered[gateKey] = true;

                var gate = Gates.GetOrAdd(gateKey, _ => new ManualResetEventSlim(false));

                // Max-block timeout: if the test aborts/asserts before ever calling OpenGate, this
                // throws (loudly, caught and logged by RunLaneConsumer's own try/catch) instead of
                // hanging the lane consumer -- and therefore the actor system's shutdown -- forever.
                if (!gate.Wait(TimeSpan.FromSeconds(60)))
                    throw new TimeoutException($"GateBlockingSerializer: gate [{gateKey}] was never opened within 60s.");

                return new GatedInt(gateKey, value);
            }

            /// <summary><see langword="true"/> once at least one <see cref="FromBinary"/> call for <paramref name="gateKey"/> has reached (and is blocked on) its gate.</summary>
            public static bool HasEntered(string gateKey) => Entered.ContainsKey(gateKey);

            /// <summary>Opens (permanently -- never <c>Reset</c>) the gate for <paramref name="gateKey"/>, releasing every <see cref="FromBinary"/> call blocked on it, past and future.</summary>
            public static void OpenGate(string gateKey) => Gates.GetOrAdd(gateKey, _ => new ManualResetEventSlim(false)).Set();
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>Watches <paramref name="target"/> at construction; forwards <see cref="Terminated"/> to <paramref name="notify"/> (if supplied).</summary>
        private sealed class PlainWatcher : ReceiveActor
        {
            public PlainWatcher(IActorRef target, IActorRef? notify = null)
            {
                Context.Watch(target);
                Receive<Terminated>(_ => notify?.Tell("terminated"));
            }
        }

        private sealed record GetHistory;
        private sealed record History(int[] Values);

        /// <summary>Records every <see langword="int"/> it receives, in receipt order; answers <see cref="GetHistory"/> with the recorded sequence so far.</summary>
        private sealed class OrderRecorder : ReceiveActor
        {
            private readonly List<int> _received = new();

            public OrderRecorder()
            {
                Receive<int>(i => _received.Add(i));
                // GatedInt (see the backpressure-parking spec below) unwraps to the same recorded
                // int history -- so the SAME OrderRecorder/GetHistory assertions this file already
                // uses for order-preservation work unchanged for that spec too.
                Receive<GatedInt>(g => _received.Add(g.Value));
                Receive<GetHistory>(_ => Sender.Tell(new History(_received.ToArray())));
            }
        }

        [Fact(DisplayName = "Inbound lanes: settings parse the configured lane count, and a real accepted Ordinary connection actually materializes exactly that many lane consumer loops")]
        public async Task Should_Parse_And_Materialize_The_Configured_Lane_Count()
        {
            const int lanes = 4;

            var arteryConfig = ConfigurationFactory.ParseString($"akka.remote.artery.advanced.inbound-lanes = {lanes}")
                .WithFallback(RemoteConfigFactory.Default())
                .GetConfig("akka.remote.artery");
            new ArterySettings(arteryConfig).InboundLanes.Should().Be(lanes);

            var observedLaneCounts = new ConcurrentBag<int>();
            var setup = BootstrapSetup.Create().WithConfig(ArteryConfig(lanes))
                .And(new ArteryTransportSetup(onInboundLanesInitialized: observedLaneCounts.Add));

            var systemB = ActorSystem.Create("ArteryInboundLanesCountB", setup);
            var systemA = ActorSystem.Create("ArteryInboundLanesCountA", ArteryConfig());
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var echoRef = await systemA.ActorSelection(RemotePath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echoRef.Tell("ping", probe.Ref);
                await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));

                await AwaitAssertAsync(() =>
                {
                    observedLaneCounts.Should().NotBeEmpty("the accepted Ordinary connection must have materialized its lane machinery");
                    observedLaneCounts.Should().OnlyContain(n => n == lanes, "every materialized lane set must contain exactly the configured lane count");
                }, TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Inbound lanes: concurrent senders to ~16 distinct recipients at inbound-lanes=4 each preserve their own send order, despite fan-out across lanes")]
        public async Task Should_Preserve_PerRecipient_Order_Across_Concurrent_Senders_With_Lanes()
        {
            const int recipientCount = 16;
            const int messagesPerRecipient = 300;

            var systemB = ActorSystem.Create("ArteryInboundLanesOrderB", ArteryConfig(inboundLanes: 4));
            var systemA = ActorSystem.Create("ArteryInboundLanesOrderA", ArteryConfig());
            try
            {
                var recorders = new IActorRef[recipientCount];
                for (var r = 0; r < recipientCount; r++)
                    recorders[r] = systemB.ActorOf(Props.Create(() => new OrderRecorder()), $"recorder-{r}");

                // Resolve every recipient's RemoteActorRef from A up front (outside the concurrent
                // send loop below) -- purely local address-based resolution, no extra round trips.
                var remoteRefs = new IActorRef[recipientCount];
                for (var r = 0; r < recipientCount; r++)
                    remoteRefs[r] = await systemA.ActorSelection(RemotePath(systemB, $"recorder-{r}")).ResolveOne(TimeSpan.FromSeconds(10));

                // Concurrent senders: one Task per recipient, all racing onto the SAME association's
                // single outbound queue/connection at once -- exactly the interleaving-across-lanes
                // scenario this test exists to prove is still per-recipient-ordered on the receiving
                // (lanes=4) side.
                var sendTasks = Enumerable.Range(0, recipientCount).Select(r => Task.Run(() =>
                {
                    for (var i = 0; i < messagesPerRecipient; i++)
                        remoteRefs[r].Tell(i, ActorRefs.NoSender);
                })).ToArray();

                await Task.WhenAll(sendTasks).WaitAsync(TimeSpan.FromSeconds(30));

                var expected = Enumerable.Range(0, messagesPerRecipient).ToArray();

                for (var r = 0; r < recipientCount; r++)
                {
                    var probe = CreateTestProbe(systemA);
                    await AwaitAssertAsync(async () =>
                    {
                        recorders[r].Tell(new GetHistory(), probe.Ref);
                        var history = await probe.ExpectMsgAsync<History>(TimeSpan.FromSeconds(5));
                        history.Values.Should().Equal(expected,
                            $"recorder-{r} must observe its own {messagesPerRecipient} sends in EXACT send order, regardless of concurrent traffic to other recipients on other lanes");
                    }, TimeSpan.FromSeconds(30));
                }
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        [Fact(DisplayName = "Inbound lanes: handshake completion and the control-stream DeathWatch path (Watch/Terminated) are unaffected at inbound-lanes=4, alongside a concurrent ordinary round trip")]
        public async Task Should_Leave_Handshake_And_Control_Stream_Unaffected_At_Lanes4()
        {
            var systemB = ActorSystem.Create("ArteryInboundLanesHandshakeB", ArteryConfig(inboundLanes: 4));
            var systemA = ActorSystem.Create("ArteryInboundLanesHandshakeA", ArteryConfig(inboundLanes: 4));
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var watchTarget = systemB.ActorOf(Props.Create(() => new Echo()), "watch-target");

                // Handshake completion, proven the same way every other Artery spec proves it: an
                // ordinary round trip actually arrives.
                var echoRef = await systemA.ActorSelection(RemotePath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var echoProbe = CreateTestProbe(systemA);
                echoRef.Tell("ping", echoProbe.Ref);
                await echoProbe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));

                // Control-stream DeathWatch path: Watch (outbound control from A) / Terminated
                // (delivered back to A once B's watch-target stops) -- entirely independent of the
                // Ordinary connection's lane machinery (see ArteryInboundProcessingStage's "Why
                // control/large connections and lanes=1 never touch this machinery" remarks: the
                // CONTROL connection is never lane-routed regardless of inbound-lanes).
                var watchTargetRemote = RARP.For(systemA).Provider.ResolveActorRef(RemotePath(systemB, "watch-target"));
                var terminatedProbe = CreateTestProbe(systemA);
                systemA.ActorOf(Props.Create(() => new PlainWatcher(watchTargetRemote, terminatedProbe.Ref)));

                // A concurrent burst of ordinary traffic to the SAME echo actor while the watch
                // above is in flight -- proves the lane path and the control path do not interfere
                // with each other.
                var burstProbe = CreateTestProbe(systemA);
                for (var i = 0; i < 50; i++)
                    echoRef.Tell(i, burstProbe.Ref);
                for (var i = 0; i < 50; i++)
                    await burstProbe.ExpectMsgAsync(i, TimeSpan.FromSeconds(10));

                watchTarget.Tell(PoisonPill.Instance);
                await terminatedProbe.ExpectMsgAsync("terminated", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        /// <summary>
        /// Adapts <c>ArteryUnwatchShutdownRaceSpec</c>'s scenario (a mass-Unwatch termination burst
        /// racing graceful <c>ArteryRemoting.Shutdown()</c>) with <c>inbound-lanes</c> &gt; 1 on BOTH
        /// sides -- proving <see cref="ArteryInboundProcessingStage"/>'s OWN teardown (lane channels
        /// completed in <c>Logic.PostStop</c>, consumer loops draining and exiting on their own, no
        /// blocking join) does not hang or error, even while a burst of real inbound Unwatch control
        /// traffic AND a burst of in-flight ordinary lane traffic are both landing as the receiving
        /// side terminates.
        /// </summary>
        [Fact(DisplayName = "Inbound lanes: clean teardown at inbound-lanes=4 -- graceful shutdown under a mass-Unwatch burst plus concurrent in-flight ordinary lane traffic does not hang or error")]
        public async Task Should_Teardown_Cleanly_At_Lanes4_Under_Concurrent_Unwatch_And_Ordinary_Bursts()
        {
            const int watcheesPerIteration = 150;
            const int recipientCount = 8;

            for (var iteration = 0; iteration < 3; iteration++)
            {
                var systemA = ActorSystem.Create($"ArteryInboundLanesTeardownA{iteration}", ArteryConfig(inboundLanes: 4));
                var systemB = ActorSystem.Create($"ArteryInboundLanesTeardownB{iteration}", ArteryConfig(inboundLanes: 4));
                try
                {
                    for (var i = 0; i < watcheesPerIteration; i++)
                        systemB.ActorOf(Props.Create(() => new Echo()), $"target-{i}");

                    var recorders = new IActorRef[recipientCount];
                    for (var r = 0; r < recipientCount; r++)
                        recorders[r] = systemA.ActorOf(Props.Create(() => new OrderRecorder()), $"recorder-{r}");

                    // Warm up the association first (one real request/reply round trip), same idiom
                    // as ArteryUnwatchShutdownRaceSpec.
                    var warmupTarget = await systemA.ActorSelection(RemotePath(systemB, "target-0")).ResolveOne(TimeSpan.FromSeconds(10));
                    var warmupProbe = CreateTestProbe(systemA);
                    warmupTarget.Tell("warmup", warmupProbe.Ref);
                    await warmupProbe.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));

                    var targets = new IActorRef[watcheesPerIteration];
                    for (var i = 0; i < watcheesPerIteration; i++)
                        targets[i] = RARP.For(systemA).Provider.ResolveActorRef(RemotePath(systemB, $"target-{i}"));

                    for (var i = 0; i < watcheesPerIteration; i++)
                        systemA.ActorOf(Props.Create(() => new PlainWatcher(targets[i], ActorRefs.NoSender)));

                    var remoteWatcher = RARP.For(systemA).Provider.RemoteWatcher;
                    var statsProbe = CreateTestProbe(systemA);
                    await AwaitAssertAsync(async () =>
                    {
                        remoteWatcher.Tell(new RemoteWatcher.Stats(0, 0), statsProbe.Ref);
                        var stats = await statsProbe.ExpectMsgAsync<RemoteWatcher.Stats>(TimeSpan.FromSeconds(3));
                        stats.WatchingRefs.Count.Should().BeGreaterOrEqualTo(watcheesPerIteration);
                    }, TimeSpan.FromSeconds(10));

                    // Concurrent in-flight ORDINARY lane traffic from B -> A's recorders, still being
                    // sent as A begins tearing down -- exercises this port's inbound lane teardown
                    // (Logic.PostStop) on systemA's side WHILE systemA's own Shutdown() is racing the
                    // RemoteWatcher Unwatch drain above.
                    var remoteRecorders = new IActorRef[recipientCount];
                    for (var r = 0; r < recipientCount; r++)
                        remoteRecorders[r] = RARP.For(systemB).Provider.ResolveActorRef(RemotePath(systemA, $"recorder-{r}"));

                    var burstCts = new System.Threading.CancellationTokenSource();
                    var burstTasks = Enumerable.Range(0, recipientCount).Select(r => Task.Run(async () =>
                    {
                        var i = 0;
                        while (!burstCts.IsCancellationRequested)
                        {
                            remoteRecorders[r].Tell(i++, ActorRefs.NoSender);
                            await Task.Delay(5);
                        }
                    })).ToArray();

                    // THE regression assertion: no control-queue-full ERROR anywhere during graceful
                    // termination (same wording as ArteryUnwatchShutdownRaceSpec), AND -- new for
                    // this port's inbound lanes -- Terminate() itself must actually complete (a
                    // liveness await, not a timing measurement): a lane consumer loop or a parked
                    // backpressured write that never unblocks would hang shutdown instead.
                    await CreateEventFilter(systemA).Error(contains: "is full (capacity").ExpectAsync(0, async () =>
                    {
                        await systemA.Terminate().AwaitWithTimeout(20.Seconds());
                    });

                    burstCts.Cancel();
                    await Task.WhenAll(burstTasks).WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                    await systemB.Terminate().AwaitWithTimeout(15.Seconds());
                }
            }
        }

        /// <summary>
        /// Covers the ONE path none of the specs above reach: <c>ArteryInboundProcessingStage.Logic.TryRouteToLane</c>
        /// returning <see langword="false"/> (a lane's channel is genuinely full) -&gt; the item is
        /// PARKED (<c>_writeInFlight = true</c>) -&gt; <c>DeliverOrPull</c>/<c>OnPull</c> suppress
        /// further <c>Pull</c> on the connection (connection-level backpressure) -&gt;
        /// <c>OnLaneWriteAvailable</c> (the <c>GetAsyncCallback</c>-wrapped continuation of
        /// <c>ChannelWriter.WaitToWriteAsync</c>) resumes draining once the lane frees up.
        ///
        /// <para>
        /// <b>Why deserialization, not the recipient, is what has to block.</b> A slow ACTOR cannot
        /// fill a lane channel: the lane consumer's final step is a fire-and-forget <c>Tell</c> into
        /// an unbounded mailbox (<c>ArteryRemoting.DispatchOrdinaryMessage</c>), so the consumer loop
        /// never stalls waiting on a recipient, no matter how slow that recipient is. The only
        /// synchronous, blockable step in a lane consumer's loop is
        /// <see cref="Akka.Serialization.Serialization.Deserialize(System.Buffers.ReadOnlySequence{byte},int,string)"/>
        /// itself -- so <see cref="GateBlockingSerializer"/> (registered via
        /// <c>serialization-bindings</c>, exactly like every other custom-serializer spec in this
        /// repo -- never a lanes-specific mechanism) blocks THAT, deterministically, until the test
        /// releases it.
        /// </para>
        ///
        /// <para>
        /// <b>Why this reliably parks (not merely "usually").</b> <c>inbound-lane-buffer-size</c> is
        /// set to 4 and <paramref name="gatedMessageCount"/>-many (32) gated messages are sent to
        /// ONE recipient, ALL hashing to the SAME lane (recipient hashing is stable per recipient --
        /// see <c>ArteryInboundProcessingStage.Logic.LaneFor</c>) -- so with that lane's consumer
        /// permanently blocked (on message 0) until the gate opens, capacity fills after 4 further
        /// messages queue up behind it and the 6th <c>TryRouteToLane</c> call is GUARANTEED to park,
        /// purely as a matter of the source (<see cref="System.Threading.Channels.BoundedChannelOptions"/>
        /// capacity vs. message count), never a timing race. The 8 PLAIN messages to a SECOND
        /// recipient (whichever lane it happens to hash onto -- the wire recipient path includes the
        /// resolved actor's uid, so the lane is not something this test can predict or force ahead of
        /// creating it, and it does not need to) are sent strictly AFTER all 32 gated ones in this
        /// association's one shared outbound queue/connection, so they are guaranteed to be
        /// UNREACHABLE by the single frame parser until the park clears too: <c>DrainReadyFramesLaneMode</c>
        /// stops draining the WHOLE connection the instant anything parks (see
        /// <see cref="ArteryInboundProcessingStage"/>'s "single-parked-item" design), so neither
        /// recipient can have received anything before the gate opens -- not a probabilistic
        /// argument, a structural one that holds regardless of which lane either recipient lands on.
        /// </para>
        ///
        /// <para>
        /// <b>Assertions (maintainer policy: no wall-clock thresholds).</b> (a) no message lost --
        /// all 40 (32 gated + 8 plain) arrive once the gate releases; (b) per-recipient send order is
        /// preserved for both; (c) both <see cref="ActorSystem"/>s <c>Terminate()</c> cleanly
        /// afterward (teardown liveness -- a parked write or blocked lane consumer that never
        /// resumed would hang this instead). The brief <see cref="Task.Delay(TimeSpan)"/> below is
        /// pure PACING (same idiom as the burst loop's <c>Task.Delay(5)</c> in
        /// <see cref="Should_Teardown_Cleanly_At_Lanes4_Under_Concurrent_Unwatch_And_Ordinary_Bursts"/>
        /// above) giving the fused stage's own interpreter thread -- which decodes/routes frames
        /// concurrently with, and without waiting on, the blocked lane consumer -- a generous margin
        /// to reach and park at message 5 before the gate opens; it is never used to assert a
        /// pass/fail condition.
        /// </para>
        /// </summary>
        [Fact(DisplayName = "Inbound lanes: a slow DESERIALIZATION parks the fused stage on lane backpressure (TryRouteToLane parks -> Pull suppressed -> OnLaneWriteAvailable resumes) -- releasing the gate afterward delivers every message, on both lanes, in order, and both systems terminate cleanly")]
        public async Task Should_Recover_All_Messages_After_A_Deserialization_Blocked_Lane_Parks_On_Backpressure()
        {
            const int inboundLanes = 2;
            const int laneBufferSize = 4; // small on purpose -- comfortably exceeded by gatedMessageCount below (see the type-level remarks for why this guarantees a park, not merely risks one).
            const int gatedMessageCount = 32;
            const int plainMessageCount = 8;
            var gateKey = Guid.NewGuid().ToString("N");

            var systemB = ActorSystem.Create("ArteryInboundLanesParkB", GatedConfig(inboundLanes, laneBufferSize));
            var systemA = ActorSystem.Create("ArteryInboundLanesParkA", GatedConfig(inboundLanes: 1, inboundLaneBufferSize: laneBufferSize));
            try
            {
                // "gated" is bound to GateBlockingSerializer; "plain" uses ordinary int messages
                // through the default serializer. Whichever lane each hashes onto is incidental --
                // not load-bearing for correctness (see the type-level remarks: EVERY lane recovers
                // the same way once the gate opens, and the send order below guarantees neither
                // recipient sees anything before then regardless of lane assignment).
                var gatedRecorder = systemB.ActorOf(Props.Create(() => new OrderRecorder()), "gated");
                var plainRecorder = systemB.ActorOf(Props.Create(() => new OrderRecorder()), "plain");

                // ResolveOne is itself a request/reply round trip -- it doubles as this association's
                // warm-up (handshake completion) before the real burst below, same as every other
                // spec in this file.
                var gatedRemote = await systemA.ActorSelection(RemotePath(systemB, "gated")).ResolveOne(TimeSpan.FromSeconds(10));
                var plainRemote = await systemA.ActorSelection(RemotePath(systemB, "plain")).ResolveOne(TimeSpan.FromSeconds(10));

                try
                {
                    // All 32 gated messages, then all 8 plain ones -- deliberately NOT interleaved,
                    // so every plain frame sits strictly BEHIND every gated frame in this
                    // association's one shared outbound queue/connection (see the type-level
                    // remarks: this is what makes "neither recipient receives anything before the
                    // gate opens" a structural guarantee rather than a timing race).
                    for (var i = 0; i < gatedMessageCount; i++)
                        gatedRemote.Tell(new GatedInt(gateKey, i), ActorRefs.NoSender);

                    for (var i = 0; i < plainMessageCount; i++)
                        plainRemote.Tell(i, ActorRefs.NoSender);

                    // Progress wait (not a wall-clock one): confirms message 0 has actually reached
                    // FromBinary and is blocked, i.e. the parking machinery is now live.
                    await AwaitAssertAsync(
                        () => GateBlockingSerializer.HasEntered(gateKey).Should().BeTrue(
                            "the gated recipient's first message must have reached FromBinary and be blocked on the gate"),
                        TimeSpan.FromSeconds(10));

                    // Pacing only -- see the type-level remarks' last paragraph.
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
                finally
                {
                    // Always release the gate, even if the wait above itself threw -- otherwise a
                    // failed assertion here would leave the blocked lane consumer parked for its own
                    // internal 60s max-block timeout, needlessly extending a failing run (and,
                    // without this, risking a hang in the Terminate() calls below).
                    GateBlockingSerializer.OpenGate(gateKey);
                }

                // (a) no message lost, (b) exact per-recipient send order, for BOTH lanes.
                var gatedProbe = CreateTestProbe(systemA);
                await AwaitAssertAsync(async () =>
                {
                    gatedRecorder.Tell(new GetHistory(), gatedProbe.Ref);
                    var history = await gatedProbe.ExpectMsgAsync<History>(TimeSpan.FromSeconds(5));
                    history.Values.Should().Equal(Enumerable.Range(0, gatedMessageCount),
                        "every gated message must arrive, in exact send order, once the gate releases");
                }, TimeSpan.FromSeconds(30));

                var plainProbe = CreateTestProbe(systemA);
                await AwaitAssertAsync(async () =>
                {
                    plainRecorder.Tell(new GetHistory(), plainProbe.Ref);
                    var history = await plainProbe.ExpectMsgAsync<History>(TimeSpan.FromSeconds(5));
                    history.Values.Should().Equal(Enumerable.Range(0, plainMessageCount),
                        "the OTHER lane's traffic -- queued up behind the SAME connection-wide park -- must also fully recover, in order, once the gate releases");
                }, TimeSpan.FromSeconds(30));
            }
            finally
            {
                // (c) teardown liveness: Terminate() must actually complete -- a still-parked write
                // or a lane consumer stuck on an un-opened gate would hang this instead.
                await systemA.Terminate().AwaitWithTimeout(20.Seconds());
                await systemB.Terminate().AwaitWithTimeout(20.Seconds());
            }
        }
    }
}
