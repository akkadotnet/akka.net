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
    /// Tests inbound quarantine enforcement on the lane path, which is active when
    /// <c>akka.remote.artery.advanced.inbound-lanes</c> is more than 1.
    ///
    /// <para>
    /// Lane traffic does not go through the connection sink that holds
    /// <see cref="InboundQuarantineCheckStage"/>. <c>ProcessFrameLaneMode</c> in
    /// <c>ArteryInboundProcessingStage</c> therefore does the same check in its own code. This spec
    /// tests that code.
    /// </para>
    ///
    /// <para>
    /// The receiving system uses 2 lanes. The <see cref="ArteryTransportSetup"/> hook reports the
    /// lane count, and this spec asserts on it. Without that assertion, the connection could use
    /// the sink path and the test would still pass, which would prove nothing about the lane path.
    /// </para>
    ///
    /// <para>
    /// Policy for maintainers, the same as <see cref="ArteryInboundLanesSpec"/>: do not assert on
    /// elapsed time. Each assertion tests progress, order or completion, or it waits for a
    /// condition. The one exception is the short <c>ExpectNoMsgAsync</c> that tests the messages
    /// which must not arrive.
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
