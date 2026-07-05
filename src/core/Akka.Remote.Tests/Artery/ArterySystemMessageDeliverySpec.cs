//-----------------------------------------------------------------------
// <copyright file="ArterySystemMessageDeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
// Akka.Remote.SysAck/Akka.Remote.SysNack (classic AckedDelivery.cs) are enclosing-namespace types of
// Akka.Remote.Tests.Artery and would otherwise shadow Akka.Remote.Artery.SysAck/SysNack (design.md gate
// G3) in unqualified lookups -- alias to the Artery ones explicitly.
using SysAck = Akka.Remote.Artery.Ack;
using SysNack = Akka.Remote.Artery.Nack;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Two-ActorSystem end-to-end tests for reliable system-message delivery (design.md gate G3,
    /// "Reliable system-message delivery"). Mirrors <c>ArteryControlStreamSpec</c>/<c>ArteryTransportSpec</c>'s
    /// patterns -- async TestKit only, ephemeral ports, try/finally shutdown.
    /// </summary>
    public class ArterySystemMessageDeliverySpec : AkkaSpec
    {
        public ArterySystemMessageDeliverySpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(TimeSpan? resendInterval = null, TimeSpan? giveUpAfter = null) =>
            ConfigurationFactory.ParseString($$"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                {{(resendInterval is { } ri ? $"akka.remote.artery.advanced.system-message-resend-interval = {(long)ri.TotalMilliseconds}ms" : "")}}
                {{(giveUpAfter is { } gu ? $"akka.remote.artery.advanced.give-up-system-message-after = {(long)gu.TotalMilliseconds}ms" : "")}}
                """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Fault-injection hook (design.md gate G3 correctness suite, "DeathWatch end-to-end under
        /// loss") -- drops every outbound <see cref="SysAck"/>/<see cref="SysNack"/> unconditionally, for
        /// the whole test. Only SysAck/SysNack are best-effort (design.md invariant 6); the actual
        /// <see cref="SystemMessageEnvelope"/> traffic (Watch/DeathWatchNotification) is untouched,
        /// so this proves delivery does not depend on any SysAck/SysNack ever arriving -- AND, since the
        /// sender's resend timer will keep resending the never-acked envelope forever, it also
        /// exercises the inbound acker's DEDUP (exactly one Terminated must be delivered, not one
        /// per resend).
        /// </summary>
        private static bool DropAcksAndNacks(object message) => message is SysAck or SysNack;

        [Fact(DisplayName = "DeathWatch end-to-end under induced ack loss: Terminated still arrives exactly once, despite every SysAck/SysNack reply being silently dropped")]
        public async Task Should_Deliver_Terminated_Exactly_Once_Despite_Induced_Ack_Loss()
        {
            var resendInterval = TimeSpan.FromMilliseconds(150);
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(ArteryConfig(resendInterval: resendInterval)),
                new ArteryTransportSetup(dropOutboundControlMessage: DropAcksAndNacks));

            var systemA = ActorSystem.Create("ArteryDeathWatchLossA", setup);
            var systemB = ActorSystem.Create("ArteryDeathWatchLossB", setup);
            try
            {
                var targetOnB = systemB.ActorOf(Props.Create(() => new Echo()), "target");

                var targetFromA = await systemA.ActorSelection(SelectionPath(systemB, "target")).ResolveOne(TimeSpan.FromSeconds(10));
                var terminatedProbe = CreateTestProbe(systemA);
                // The probe itself watches the target directly (TestProbe.WatchAsync) -- Terminated
                // is an AutoReceiveMessage (design.md is not involved here, this is a general Akka.NET
                // semantic): forwarding a Terminated via an ordinary Tell to an unrelated actor (e.g. a
                // separate "Watcher" actor relaying to the probe) is silently swallowed by
                // ActorCell.ReceivedTerminated's "am I actually watching this ref" guard unless the
                // RECEIVING actor itself performed the Watch, so the probe must watch directly.
                await terminatedProbe.WatchAsync(targetFromA);

                // Give the Watch (A -> B, reliable system message) a moment to actually land before
                // killing the target -- otherwise this is racing the Watch's own resend/delivery.
                await Task.Delay(resendInterval + resendInterval);

                targetOnB.Tell(PoisonPill.Instance);

                var terminated = await terminatedProbe.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));
                terminated.ActorRef.Path.ToStringWithoutAddress().Should().Be(targetFromA.Path.ToStringWithoutAddress());

                // Sustained loss keeps the sender's resend timer firing forever (nothing is ever
                // acked) -- wait several more resend intervals and prove NO duplicate Terminated
                // shows up, i.e. the inbound acker correctly deduplicates every resent
                // DeathWatchNotification.
                await terminatedProbe.ExpectNoMsgAsync(resendInterval * 6);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
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

        [Fact(DisplayName = "control-lane non-starvation extended to system messages: the control lane keeps making progress AND a Watch/Terminated round trip completes while the ordinary stream is flooded")]
        public async Task Should_Not_Starve_System_Messages_Or_Heartbeats_Under_Ordinary_Traffic_Load()
        {
            var heartbeatInterval = TimeSpan.FromMilliseconds(250);
            var config = ArteryConfig().WithFallback(ConfigurationFactory.ParseString(
                $"akka.remote.artery.advanced.control-heartbeat-interval = {(long)heartbeatInterval.TotalMilliseconds}ms"));

            var systemA = ActorSystem.Create("ArterySysMsgNonStarvationA", config);
            var systemB = ActorSystem.Create("ArterySysMsgNonStarvationB", config);
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var targetOnB = systemB.ActorOf(Props.Create(() => new Echo()), "watch-target");

                var echoRef = await systemA.ActorSelection(SelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var targetFromA = await systemA.ActorSelection(SelectionPath(systemB, "watch-target")).ResolveOne(TimeSpan.FromSeconds(10));

                var heartbeatProbe = CreateTestProbe(systemB);
                ((ArteryRemoting)RARP.For(systemB).Provider.Transport).SubscribeControl(new DelegateControlSubscriber(heartbeatProbe.Ref));

                var terminatedProbe = CreateTestProbe(systemA);
                await terminatedProbe.WatchAsync(targetFromA);

                // Baseline: the control lane must already be making progress BEFORE the flood
                // starts (no timestamps, no elapsed-time math -- just "has at least one
                // heartbeat-or-Rsp arrived").
                await heartbeatProbe.FishForMessageAsync(IsControlLaneProgress, TimeSpan.FromSeconds(10));

                // Flood the ordinary channel while the Watch (a system message) is still in
                // flight. floodTask is deliberately NOT awaited until the very end of the test --
                // everything below runs while it is still outstanding, so the assertions that
                // follow are verifiably concurrent with the flood (an ORDER guarantee from the
                // test's structure), not merely sequenced after it happens to finish.
                var payload = new string('x', 4096);
                const int floodCount = 5_000;
                var floodTask = Task.Run(() =>
                {
                    for (var i = 0; i < floodCount; i++)
                        echoRef.Tell(payload);
                });

                // The control lane must keep making PROGRESS during the flood window -- a small,
                // fixed number of additional heartbeat-or-Rsp elements (2: enough to prove the
                // lane is still cycling more than once, not a rate/gap claim) must still arrive
                // while the flood is outstanding.
                const int additionalProgressDuringFlood = 2;
                for (var i = 0; i < additionalProgressDuringFlood; i++)
                    await heartbeatProbe.FishForMessageAsync(IsControlLaneProgress, TimeSpan.FromSeconds(10));

                // The Watch/Terminated round trip must also COMPLETE while the flood is still
                // outstanding -- a generous liveness await (not a timing measurement).
                targetOnB.Tell(PoisonPill.Instance);
                await terminatedProbe.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));

                await floodTask;
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "graceful shutdown: terminating a system with in-flight reliable system messages completes cleanly and does not publish a spurious quarantine event")]
        public async Task Should_Shutdown_Cleanly_With_InFlight_System_Messages_Without_Quarantining()
        {
            var systemA = ActorSystem.Create("ArteryShutdownInFlightA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryShutdownInFlightB", ArteryConfig());
            try
            {
                var targetOnB = systemB.ActorOf(Props.Create(() => new Echo()), "target");
                var targetFromA = await systemA.ActorSelection(SelectionPath(systemB, "target")).ResolveOne(TimeSpan.FromSeconds(10));

                var quarantineProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(QuarantinedEvent));
                systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(ThisActorSystemQuarantinedEvent));

                // Fire a burst of Watch system messages and immediately shut down, without waiting
                // for any of them to be acknowledged -- exercises the "in-flight at Shutdown" path.
                // Documented behavior (per design.md gate G3): a graceful shutdown must complete
                // cleanly and must NOT quarantine -- unflushed system messages are simply not
                // delivered (no give-up/quarantine ceremony), a different outcome from the give-up
                // (buffer overflow / resend-timeout) path, which always quarantines. Fire-and-forget
                // plain actors are used here (not TestProbe.WatchAsync, which awaits completion --
                // the whole point is to have these still in flight, unacknowledged, at Shutdown).
                for (var i = 0; i < 20; i++)
                    systemA.ActorOf(Props.Create(() => new PlainWatcher(targetFromA)));

                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await quarantineProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        /// <summary>A minimal actor that just watches <paramref name="target"/> at construction and does nothing else.</summary>
        private sealed class PlainWatcher : ReceiveActor
        {
            public PlainWatcher(IActorRef target)
            {
                Context.Watch(target);
                Receive<Terminated>(_ => { });
            }
        }

        /// <summary>Forwards every received control message to a probe -- see <c>ArteryControlStreamSpec</c>'s identical helper.</summary>
        private sealed class DelegateControlSubscriber : IControlMessageSubscriber
        {
            private readonly IActorRef _probe;
            public DelegateControlSubscriber(IActorRef probe) => _probe = probe;
            public void ControlMessageReceived(long originUid, object message) => _probe.Tell(message);
        }
    }
}
