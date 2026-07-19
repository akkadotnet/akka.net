//-----------------------------------------------------------------------
// <copyright file="ArteryInboundLanesQuarantineSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Locks inbound quarantine enforcement on the INBOUND-LANES path specifically
    /// (<c>akka.remote.artery.advanced.inbound-lanes</c> &gt; 1): lane-routed ordinary traffic
    /// bypasses the connection sink where <see cref="InboundQuarantineCheckStage"/> sits, so
    /// <c>ArteryInboundProcessingStage.ProcessFrameLaneMode</c> inlines the same
    /// drop-and-renotify gate -- this spec is what makes that inlining load-bearing. The
    /// receiving system runs lanes = 2 with the <see cref="ArteryTransportSetup"/> lane-count
    /// observation hook asserting the lane machinery actually materialized for the sender's
    /// Ordinary connection, so the assertions below cannot silently pass through the (already
    /// separately covered) non-lane sink path.
    ///
    /// <para>
    /// <b>Maintainer policy (same as <see cref="ArteryInboundLanesSpec"/>):</b> no wall-clock
    /// thresholds -- every assertion is progress/order/completion, or a liveness await, plus the
    /// one standard short <c>ExpectNoMsgAsync</c> negative check for the not-delivered half.
    /// </para>
    /// </summary>
    public class ArteryInboundLanesQuarantineSpec : AkkaSpec
    {
        public ArteryInboundLanesQuarantineSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int inboundLanes = 1) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{inboundLanes}}
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string RemotePath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        /// <summary>Forwards every message it receives to the probe ref it was constructed with.</summary>
        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target)
            {
                ReceiveAny(msg => target.Tell(msg));
            }
        }

        [Fact(DisplayName = "Inbound lanes: at inbound-lanes=2 a quarantined uid's ordinary messages are dropped on the lane path (not delivered) and each drop reactively re-notifies the quarantined peer")]
        public async Task Should_Drop_And_Renotify_Quarantined_Origin_On_The_Lane_Path()
        {
            const int lanes = 2;

            var observedLaneCounts = new ConcurrentBag<int>();
            var receiverSetup = BootstrapSetup.Create().WithConfig(ArteryConfig(lanes))
                .And(new ArteryTransportSetup(onInboundLanesInitialized: observedLaneCounts.Add));

            var systemA = ActorSystem.Create("ArteryLanesQuarantineA", receiverSetup); // the quarantiner / receiver, lanes=2
            var systemB = ActorSystem.Create("ArteryLanesQuarantineB", ArteryConfig()); // the (to-be-)quarantined sender
            try
            {
                var aProbe = CreateTestProbe(systemA);
                systemA.ActorOf(Props.Create(() => new Forwarder(aProbe.Ref)), "recorder");

                // Sanity + handshake: B's ordinary traffic reaches A's recorder THROUGH the lane
                // path (lane machinery materialization asserted just below), and the round trip
                // completes A's handshake with B so Quarantine(uid) below acts on a known uid.
                var recorderRef = await systemB.ActorSelection(RemotePath(systemA, "recorder")).ResolveOne(TimeSpan.FromSeconds(10));
                recorderRef.Tell("before");
                await aProbe.ExpectMsgAsync("before", TimeSpan.FromSeconds(10));

                await AwaitAssertAsync(() =>
                {
                    observedLaneCounts.Should().NotBeEmpty("B's accepted Ordinary connection must have materialized A's lane machinery");
                    observedLaneCounts.Should().OnlyContain(n => n == lanes);
                }, TimeSpan.FromSeconds(10));

                // Event observation: A publishes QuarantinedEvent when the quarantine commits;
                // B publishes ThisActorSystemQuarantinedEvent per ArteryQuarantined notice received.
                var aEvents = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(aEvents.Ref, typeof(QuarantinedEvent));
                var bEvents = CreateTestProbe(systemB);
                systemB.EventStream.Subscribe(bEvents.Ref, typeof(ThisActorSystemQuarantinedEvent));

                var bAddress = RARP.For(systemB).Provider.DefaultAddress;
                var bUid = AddressUidExtension.Uid(systemB);
                RARP.For(systemA).Provider.Transport.Quarantine(bAddress, bUid);

                // Quarantine committed at A...
                (await aEvents.ExpectMsgAsync<QuarantinedEvent>(TimeSpan.FromSeconds(10))).Uid.Should().Be(bUid);
                // ...and the ONE-SHOT PROACTIVE notice reached B (also proves A->B control works).
                await bEvents.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));

                // B keeps talking: these ride B's EXISTING Ordinary connection into A's LANE path.
                for (var i = 0; i < 5; i++)
                    recorderRef.Tell($"after-{i}");

                // (b) REACTIVE re-notification: at least one further ArteryQuarantined must reach
                // B, and only the lane path can have produced it -- the five sends above are the
                // only non-heartbeat, non-notice traffic B emits, and they are lane-routed.
                await bEvents.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));

                // (a) NOT delivered: had any of the five been (wrongly) dispatched, it would have
                // been forwarded to aProbe long before the reactive notice's B-bound round trip
                // completed above -- so a short drain-check suffices.
                await aProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
