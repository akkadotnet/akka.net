//-----------------------------------------------------------------------
// <copyright file="ArteryControlStreamSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers Artery TCP remoting task group 6, "Control Stream"
    /// (<c>openspec/changes/artery-tcp-remoting/tasks.md</c>): each association's control stream
    /// is a SEPARATE connection/queue from its ordinary stream (task 6.1), inbound control
    /// processing dispatches non-handshake control messages to <see cref="IControlMessageSubscriber"/>s
    /// (task 6.2), heartbeat liveness (task 6.4), quarantine notification (task 6.5), and the
    /// non-starvation proof (task 6.6). See <c>openspec/changes/artery-tcp-remoting/design.md</c>,
    /// "Reliable system-message delivery (gate G3)" for why control isolation matters -- it is
    /// the substrate group 7's ACK/NACK stages will run on.
    /// </summary>
    public class ArteryControlStreamSpec : AkkaSpec
    {
        public ArteryControlStreamSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(TimeSpan? controlHeartbeatInterval = null)
        {
            var extra = controlHeartbeatInterval is { } interval
                ? $"akka.remote.artery.advanced.control-heartbeat-interval = {(long)interval.TotalMilliseconds}ms"
                : "";

            return ConfigurationFactory.ParseString($"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                {extra}
                """);
        }

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string EchoSelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private static ArteryRemoting TransportFor(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Forwards every received control message to a <see cref="TestProbe"/> -- lets a test
        /// observe the internal control-message hook (task 6.2) directly, independent of any one
        /// message's production-side handling.
        /// </summary>
        private sealed class ControlProbeSubscriber : IControlMessageSubscriber
        {
            private readonly IActorRef _probe;
            public ControlProbeSubscriber(IActorRef probe) => _probe = probe;
            public void ControlMessageReceived(long originUid, object message) => _probe.Tell(message);
        }

        private async Task<IActorRef> WarmUpAssociationAsync(ActorSystem systemA, ActorSystem systemB)
        {
            systemB.ActorOf(Props.Create(() => new Echo()), "echo");

            // A real ordinary round trip establishes the handshake (and therefore both systems'
            // control connections) both ways -- see design.md "Connection cardinality".
            var echoRef = await systemA.ActorSelection(EchoSelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
            var warmup = CreateTestProbe(systemA);
            echoRef.Tell("warmup", warmup.Ref);
            await warmup.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));
            return echoRef;
        }

        [Fact(DisplayName = "Control stream heartbeat: B observes ArteryHeartbeat arriving from A, and A observes B's ArteryHeartbeatRsp reply")]
        public async Task Should_Exchange_Heartbeats_Over_The_Control_Stream()
        {
            var interval = TimeSpan.FromMilliseconds(300);
            var systemA = ActorSystem.Create("ArteryHeartbeatA", ArteryConfig(interval));
            var systemB = ActorSystem.Create("ArteryHeartbeatB", ArteryConfig(interval));
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var probeOnB = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new ControlProbeSubscriber(probeOnB.Ref));

                var probeOnA = CreateTestProbe(systemA);
                TransportFor(systemA).SubscribeControl(new ControlProbeSubscriber(probeOnA.Ref));

                // B must observe a heartbeat from A -- proves the control stream's own idle timer
                // (ArteryHeartbeatStage) is live and reaches the peer.
                await probeOnB.FishForMessageAsync(msg => msg is ArteryHeartbeat, TimeSpan.FromSeconds(5));

                // A must, in turn, observe the HeartbeatRsp B's transport auto-replies with (its
                // own IControlMessageSubscriber.ControlMessageReceived handling).
                await probeOnA.FishForMessageAsync(msg => msg is ArteryHeartbeatRsp, TimeSpan.FromSeconds(5));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Quarantine notification: B publishes ThisActorSystemQuarantinedEvent and observes the exact ArteryQuarantined content A sent, when A quarantines the association")]
        public async Task Should_Notify_Peer_On_Quarantine()
        {
            var systemA = ActorSystem.Create("ArteryQuarantineNotifyA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryQuarantineNotifyB", ArteryConfig());
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var controlProbeOnB = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new ControlProbeSubscriber(controlProbeOnB.Ref));

                var eventProbeOnB = CreateTestProbe(systemB);
                systemB.EventStream.Subscribe(eventProbeOnB.Ref, typeof(ThisActorSystemQuarantinedEvent));

                var bAddress = RARP.For(systemB).Provider.DefaultAddress;
                var bUid = AddressUidExtension.Uid(systemB);
                var aAddress = RARP.For(systemA).Provider.DefaultAddress;

                TransportFor(systemA).Quarantine(bAddress, bUid);

                var received = await controlProbeOnB.ExpectMsgAsync<ArteryQuarantined>(TimeSpan.FromSeconds(10));
                received.From.Address.Should().Be(aAddress);
                received.QuarantinedUid.Should().Be(bUid);

                var evt = await eventProbeOnB.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));
                evt.RemoteAddress.Should().Be(aAddress);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Sending ordinary messages to a quarantined association drops them to dead letters instead of transmitting them")]
        public async Task Should_DeadLetter_Ordinary_Sends_To_Quarantined_Association()
        {
            var systemA = ActorSystem.Create("ArteryQuarantineGateA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryQuarantineGateB", ArteryConfig());
            try
            {
                var echoRef = await WarmUpAssociationAsync(systemA, systemB);

                var bAddress = RARP.For(systemB).Provider.DefaultAddress;
                var bUid = AddressUidExtension.Uid(systemB);

                TransportFor(systemA).Quarantine(bAddress, bUid);

                var deadLetterProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(deadLetterProbe.Ref, typeof(DeadLetter));
                try
                {
                    echoRef.Tell("should-not-be-delivered");
                    var deadLetter = await deadLetterProbe.ExpectMsgAsync<DeadLetter>(TimeSpan.FromSeconds(10));
                    deadLetter.Message.Should().Be("should-not-be-delivered");
                }
                finally
                {
                    systemA.EventStream.Unsubscribe(deadLetterProbe.Ref, typeof(DeadLetter));
                }
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        /// <summary>
        /// <see cref="ArrayPool{T}"/> that scribbles over every array it hands back to its
        /// (privately owned, test-only) inner pool -- see <c>ArteryTransportSpec</c>'s identical
        /// helper for the full rationale; duplicated here (rather than shared) to keep this spec
        /// self-contained.
        /// </summary>
        private sealed class PoisoningArrayPool : ArrayPool<byte>
        {
            private const byte PoisonByte = 0xDE;
            private readonly ArrayPool<byte> _inner = Create();

            public override byte[] Rent(int minimumLength) => _inner.Rent(minimumLength);

            public override void Return(byte[] array, bool clearArray = false)
            {
                Array.Fill(array, PoisonByte);
                _inner.Return(array, clearArray: false);
            }
        }

        /// <summary>
        /// Same poison-pool tripwire as <c>ArteryTransportSpec</c>'s ordinary-stream test, applied
        /// to the CONTROL stream. Unlike a bare heartbeat, <see cref="ArteryQuarantined"/> carries
        /// real content (an address + a uid) -- so a corrupted buffer would show up as a content
        /// mismatch or a decode failure (a missing delivery), not just silently do nothing.
        /// Repeats the (idempotent -- <c>ArteryRemoting.Quarantine</c> re-sends every call) call
        /// so multiple frames flow through the SAME two-generation
        /// <see cref="ArteryEncodeStage"/> buffer-disposal cycle on the control connection.
        /// </summary>
        [Fact(DisplayName = "Poison-pool tripwire: repeated ArteryQuarantined control notifications are not corrupted when outbound encode buffers are returned early")]
        public async Task Should_Not_Corrupt_Control_Messages_When_Outbound_Encode_Buffers_Are_Returned_Early()
        {
            var poisonPool = new PoisoningArrayPool();

            // ArteryTransportSetup (not a mutable static field) carries the pool override --
            // scoped to just these two ActorSystems -- see ArteryTransportSetup's remarks.
            var setup = BootstrapSetup.Create().WithConfig(ArteryConfig()).And(new ArteryTransportSetup(poisonPool));

            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                systemA = ActorSystem.Create("ArteryControlPoisonPoolA", setup);
                systemB = ActorSystem.Create("ArteryControlPoisonPoolB", setup);

                await WarmUpAssociationAsync(systemA, systemB);

                var controlProbeOnB = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new ControlProbeSubscriber(controlProbeOnB.Ref));

                var bAddress = RARP.For(systemB).Provider.DefaultAddress;
                var bUid = AddressUidExtension.Uid(systemB);
                var aAddress = RARP.For(systemA).Provider.DefaultAddress;
                var transportA = TransportFor(systemA);

                const int quarantineCallCount = 60;
                for (var i = 0; i < quarantineCallCount; i++)
                    transportA.Quarantine(bAddress, bUid);

                for (var i = 0; i < quarantineCallCount; i++)
                {
                    var received = await controlProbeOnB.ExpectMsgAsync<ArteryQuarantined>(TimeSpan.FromSeconds(10));
                    received.From.Address.Should().Be(aAddress,
                        "message {0}'s control-stream encode buffer must not have been reused/poisoned before its bytes were safely copied into the TCP pipe", i);
                    received.QuarantinedUid.Should().Be(bUid);
                }
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        /// <summary>
        /// Whether <paramref name="msg"/> is proof of control-lane PROGRESS on the A-&gt;B
        /// direction, as observed at B. Deliberately accepts EITHER <see cref="ArteryHeartbeat"/>
        /// OR <see cref="ArteryHeartbeatRsp"/> -- NOT heartbeat alone. Each side's own outbound
        /// idle-heartbeat timer (<c>ArteryHeartbeatStage.cs</c>'s <c>Logic.OnPush</c>, ~line 96)
        /// resets on ANY element pushed through its outbound control pipeline, including the
        /// <see cref="ArteryHeartbeatRsp"/> it sends in reply to the PEER's own on-schedule
        /// heartbeat. Two peers heartbeating each other on the same interval can therefore fall
        /// into a bistable cross-suppression pattern -- one side's steady stream of Rsps (replying
        /// to the other's heartbeats) keeps resetting that side's OWN idle clock, legitimately
        /// suppressing its self-initiated <see cref="ArteryHeartbeat"/> injection for an entire
        /// observation window even though the control lane is healthy and making real progress.
        /// An <see cref="ArteryHeartbeat"/>-only cadence assertion can flatline under this pattern
        /// with no starvation involved -- do not narrow this predicate back to heartbeat alone.
        /// </summary>
        private static bool IsControlLaneProgress(object msg) => msg is ArteryHeartbeat or ArteryHeartbeatRsp;

        /// <summary>
        /// Non-starvation, "simplest honest version" (task 6.6). Floods A-&gt;B's ORDINARY channel
        /// with a large burst of back-to-back sends (no throttling, no replies awaited) while a
        /// heartbeat subscriber on B independently watches the CONTROL stream for PROGRESS
        /// (<see cref="IsControlLaneProgress"/>) throughout the flood.
        ///
        /// <para>
        /// <b>What this proves.</b> The control stream's own materialized TCP connection + bounded
        /// channel (task 6.1) are entirely separate from the ordinary stream's -- control traffic
        /// for THIS association is not queued behind, or multiplexed with, ordinary traffic to the
        /// SAME peer. Combined with <c>AssociationRegistrySpec</c>'s queue-level isolation proof (a
        /// full ordinary channel does not block <c>TryEnqueueControl</c>), this is an end-to-end
        /// demonstration that the control lane keeps making progress while ordinary traffic is
        /// heavy.
        /// </para>
        /// <para>
        /// <b>What this does NOT prove.</b> It does not prove the ordinary channel actually reached
        /// its bounded capacity (3072) or that any message was dropped to dead letters -- forcing
        /// genuine OS-level TCP backpressure deterministically, without flakiness, would require a
        /// deliberately slow receiver at the socket level, which this test does not attempt. It
        /// also says nothing about non-starvation under G5 lanes/compression (not yet implemented)
        /// or under the reliable system-message layer's own load (group 7). This test does NOT
        /// assert any bound on the GAP between control-lane elements -- see
        /// <see cref="IsControlLaneProgress"/>'s remarks for why a wall-clock/cadence bound is not
        /// a valid property of this system even when it is perfectly healthy.
        /// </para>
        /// </summary>
        [Fact(DisplayName = "Non-starvation: the control lane keeps making progress while the ordinary stream is flooded with a large burst of traffic")]
        public async Task Should_Not_Starve_Heartbeats_Under_Ordinary_Traffic_Load()
        {
            var interval = TimeSpan.FromMilliseconds(250);
            var systemA = ActorSystem.Create("ArteryNonStarvationA", ArteryConfig(interval));
            var systemB = ActorSystem.Create("ArteryNonStarvationB", ArteryConfig(interval));
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var heartbeatProbe = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new ControlProbeSubscriber(heartbeatProbe.Ref));

                // Baseline: the control lane must already be making progress BEFORE the flood
                // starts (no timestamps, no elapsed-time math -- just "has at least one
                // heartbeat-or-Rsp arrived").
                await heartbeatProbe.FishForMessageAsync(IsControlLaneProgress, TimeSpan.FromSeconds(10));

                // Flood the ordinary channel: a large burst of sizable, back-to-back sends with no
                // sender/no reply expected, so nothing throttles the flooding loop waiting on echoes.
                // 5,000 sends comfortably exceeds the per-association ordinary queue's default
                // capacity (3072) several times over. floodTask is deliberately NOT awaited until
                // the very end -- the progress check below runs while it is still outstanding (an
                // ORDER guarantee from the test's structure, not a timing measurement).
                var floodTarget = systemA.ActorSelection(EchoSelectionPath(systemB, "echo"));
                var payload = new string('x', 4096);
                const int floodCount = 5_000;

                var floodTask = Task.Run(() =>
                {
                    for (var i = 0; i < floodCount; i++)
                        floodTarget.Tell(payload);
                });

                // The control lane must keep making PROGRESS during the flood window -- a small,
                // fixed number of additional heartbeat-or-Rsp elements (3: enough to prove the
                // lane is still cycling multiple times, not a rate/gap claim) must still arrive
                // while the flood is outstanding.
                const int additionalProgressDuringFlood = 3;
                for (var i = 0; i < additionalProgressDuringFlood; i++)
                    await heartbeatProbe.FishForMessageAsync(IsControlLaneProgress, TimeSpan.FromSeconds(10));

                await floodTask;
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
