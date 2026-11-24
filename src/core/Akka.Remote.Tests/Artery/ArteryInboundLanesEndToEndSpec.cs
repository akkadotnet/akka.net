//-----------------------------------------------------------------------
// <copyright file="ArteryInboundLanesEndToEndSpec.cs" company="Akka.NET Project">
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
    /// Two real systems over Artery TCP, receiving with 1 inbound lane (the default, where the
    /// processing stage checks and dispatches ordinary messages itself) and with 4 lanes. Quarantine
    /// has to be enforced at both.
    /// </summary>
    public class ArteryInboundLanesEndToEndSpec : AkkaSpec
    {
        public ArteryInboundLanesEndToEndSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ConfigFor(int lanes) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{lanes}}
            # Heartbeats from a quarantined peer also trigger a notice, so keep them out of the test
            # window: the notices below can then only come from dropped ordinary messages.
            akka.remote.artery.advanced.control-heartbeat-interval = 1h
            """);

        private static Address AddressFor(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;
        private static string PathFor(ActorSystem system, string name) => $"{AddressFor(system)}/user/{name}";

        private static async Task TerminateAsync(ActorSystem? system)
        {
            if (system is not null)
                await system.Terminate().AwaitWithTimeout(10.Seconds());
        }

        /// <summary>Forwards every message it receives to the probe it was given.</summary>
        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target) => ReceiveAny(message => target.Tell(message));
        }

        private (ActorSystem Receiver, ActorSystem Peer, ConcurrentBag<int> LaneCounts) StartPair(int lanes)
        {
            var laneCounts = new ConcurrentBag<int>();
            var setup = BootstrapSetup.Create().WithConfig(ConfigFor(lanes))
                .And(new ArteryTransportSetup(onInboundLanesInitialized: laneCounts.Add));
            var receiver = ActorSystem.Create("LanesE2EReceiver", setup);
            var peer = ActorSystem.Create("LanesE2EPeer", ConfigFor(lanes));
            return (receiver, peer, laneCounts);
        }

        /// <summary>At more than one lane, shows that the receiver really used the lane path.</summary>
        private Task AssertLanesInUse(int lanes, ConcurrentBag<int> laneCounts) => lanes == 1
            ? Task.CompletedTask
            : AwaitAssertAsync(() => laneCounts.Should().NotBeEmpty().And.OnlyContain(n => n == lanes), 10.Seconds());

        [Theory(DisplayName = "A quarantined peer's ordinary messages are not delivered and the peer is told")]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Should_DropOrdinaryMessagesAndNotifyPeer_When_PeerIsQuarantined(int lanes)
        {
            var (receiver, peer, laneCounts) = StartPair(lanes);
            try
            {
                var received = CreateTestProbe(receiver);
                receiver.ActorOf(Props.Create(() => new Forwarder(received.Ref)), "recorder");
                var recorder = await peer.ActorSelection(PathFor(receiver, "recorder")).ResolveOne(10.Seconds());
                recorder.Tell("before");
                await received.ExpectMsgAsync("before", 10.Seconds());
                await AssertLanesInUse(lanes, laneCounts);

                var quarantined = CreateTestProbe(receiver);
                receiver.EventStream.Subscribe(quarantined.Ref, typeof(QuarantinedEvent));
                var told = CreateTestProbe(peer);
                peer.EventStream.Subscribe(told.Ref, typeof(ThisActorSystemQuarantinedEvent));

                var peerUid = AddressUidExtension.Uid(peer);
                RARP.For(receiver).Provider.Transport.Quarantine(AddressFor(peer), peerUid);
                (await quarantined.ExpectMsgAsync<QuarantinedEvent>(10.Seconds())).Uid.Should().Be(peerUid);
                await told.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(10.Seconds());

                // Each message the receiver drops re-notifies the peer, so a second notice shows that
                // the messages sent after the first one were dropped, not just delayed.
                for (var i = 0; i < 5; i++)
                    recorder.Tell($"after-{i}");
                await told.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(10.Seconds());
                await received.ExpectNoMsgAsync(300.Milliseconds());
            }
            finally
            {
                await Task.WhenAll(TerminateAsync(receiver), TerminateAsync(peer));
            }
        }
    }
}
