//-----------------------------------------------------------------------
// <copyright file="ArteryNumericRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Ask over Artery with <c>akka.remote.artery.advanced.numeric-refs</c> on, off and mixed, and the owner-UID
    /// fence that keeps a reply for a previous incarnation of the asker away from the new one.
    /// </summary>
    public class ArteryNumericRefSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public ArteryNumericRefSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(bool numeric, int port = 0, int lanes = 1, string extra = "") => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.advanced.numeric-refs = {{(numeric ? "on" : "off")}}
            akka.remote.artery.advanced.outbound-lanes = {{lanes}}
            akka.remote.artery.advanced.inbound-lanes = {{lanes}}
            akka.remote.artery.advanced.outbound-restart-backoff = 300ms
            akka.remote.artery.advanced.control-heartbeat-interval = 500ms
            akka.remote.watch-failure-detector.heartbeat-interval = 200ms
            {{extra}}
            """);

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        private static ArteryRemoting TransportOf(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private sealed class Responder : ReceiveActor
        {
            public Responder(Action<object> report)
            {
                Receive<string>(s => s == "ping", _ =>
                {
                    report(Sender);
                    Sender.Tell("pong");
                });
                Receive<string>(s => s == "hold", _ => report(Sender));
                Receive<string>(s => s == "echo", _ => Sender.Tell("pong"));
            }
        }

        private static async Task<T> Next<T>(BlockingCollection<object> reports)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (reports.TryTake(out var item, 100) && item is T typed)
                    return typed;
                await Task.Yield();
            }

            throw new TimeoutException($"No {typeof(T).Name} reported within {Timeout}.");
        }

        private static string ResponderPath(ActorSystem system) => $"{AddressOf(system)}/user/responder";

        private (ActorSystem Asker, ActorSystem Responder, BlockingCollection<object> Reports) StartPair(
            bool askerNumeric, bool responderNumeric, int lanes = 1, string extra = "")
        {
            var asker = ActorSystem.Create("Asker", ArteryConfig(askerNumeric, lanes: lanes, extra: extra));
            var responder = ActorSystem.Create("Responder", ArteryConfig(responderNumeric, lanes: lanes, extra: extra));
            var reports = new BlockingCollection<object>();
            responder.ActorOf(Props.Create(() => new Responder(o => reports.Add(o))), "responder");
            return (asker, responder, reports);
        }

        private static Task ShutdownAll(params ActorSystem[] systems) => Task.WhenAll(systems.Select(s => s.Terminate()));

        private static Task<object> AskResponder(ActorSystem asker, ActorSystem responder, string message, TimeSpan? timeout = null) =>
            asker.ActorSelection(ResponderPath(responder)).Ask<object>(message, timeout ?? Timeout);


        [Theory(DisplayName = "Should_complete_an_Ask_over_Artery_When_numeric_refs_are_on_off_or_mixed")]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task Should_complete_an_Ask_over_Artery_When_numeric_refs_are_on_off_or_mixed(bool askerNumeric, bool responderNumeric)
        {
            var (asker, responder, reports) = StartPair(askerNumeric, responderNumeric);
            try
            {
                for (var i = 0; i < 20; i++)
                    (await AskResponder(asker, responder, "ping")).Should().Be("pong");

                var sender = await Next<IActorRef>(reports);

                // The sender is the lightweight numeric ref exactly when the ASKING side writes numeric shapes.
                if (askerNumeric)
                    sender.Should().BeOfType<RemoteTempActorRef>();
                else
                    sender.Should().NotBeOfType<RemoteTempActorRef>();
                sender.Path.Elements.Should().Equal("temp", sender.Path.Name);
            }
            finally
            {
                await ShutdownAll(asker, responder);
            }
        }


        /// <summary>Repeats a short Ask until one completes: progress-based, no fixed wait.</summary>
        private static async Task WaitForRoundTrip(ActorSystem asker, ActorSystem responder)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                try
                {
                    (await AskResponder(asker, responder, "echo", TimeSpan.FromSeconds(1))).Should().Be("pong");
                    return;
                }
                catch (AskTimeoutException) when (DateTime.UtcNow < deadline)
                {
                }
            }
        }

        private static async Task<ActorSystem> RestartOnSamePort(int port)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return ActorSystem.Create("Asker", ArteryConfig(true, port));
                }
                catch when (attempt < 40)
                {
                    await Task.Delay(250);
                }
            }
        }

        [Fact(DisplayName = "Should_not_deliver_a_reply_to_the_new_incarnation_When_the_asker_restarts_on_the_same_port")]
        public async Task Should_not_deliver_a_reply_to_the_new_incarnation_When_the_asker_restarts_on_the_same_port()
        {
            var asker = ActorSystem.Create("Asker", ArteryConfig(true));
            var responder = ActorSystem.Create("Responder", ArteryConfig(true));
            var reports = new BlockingCollection<object>();
            responder.ActorOf(Props.Create(() => new Responder(o => reports.Add(o))), "responder");
            ActorSystem? restarted = null;
            try
            {
                var port = AddressOf(asker).Port!.Value;
                _ = AskResponder(asker, responder, "hold");
                var oldSender = (RemoteTempActorRef)await Next<IActorRef>(reports);
                await asker.Terminate();

                restarted = await RestartOnSamePort(port);
                var deadLetters = CreateTestProbe(restarted);
                restarted.EventStream.Subscribe(deadLetters, typeof(DeadLetter));

                // Wait until replies to the new incarnation flow (a frame written while the responder still holds
                // its connection to the old incarnation would be lost), then leave an Ask pending.
                await WaitForRoundTrip(restarted, responder);
                var newAsk = AskResponder(restarted, responder, "hold");
                var newSender = (RemoteTempActorRef)await Next<IActorRef>(reports);
                newSender.OwnerUid.Should().NotBe(oldSender.OwnerUid);

                // Force the collision instead of relying on both incarnations having consumed counters alike: a
                // reply naming the OLD incarnation's uid and the NEW Ask's counter, delivered to the new system.
                var association = TransportOf(responder).Registry.AssociationFor(AddressOf(restarted));
                association.TryEnqueueOutbound(
                    new OutboundEnvelope("stale-reply", null, null, null,
                        ArteryNumericRef.ForTemporary(oldSender.OwnerUid, newSender.TempId)), 0).Should().BeTrue();
                await deadLetters.FishForMessageAsync<DeadLetter>(d => d.Message is "stale-reply", Timeout);
                newAsk.IsCompleted.Should().BeFalse("the stale reply was dead-lettered by the receiver, so it cannot have completed the Ask");

                newSender.Tell("for-the-new-incarnation", ActorRefs.NoSender);
                (await newAsk).Should().Be("for-the-new-incarnation");
            }
            finally
            {
                await ShutdownAll(new[] { responder, restarted! }.Where(s => s is not null).ToArray());
                if (!asker.WhenTerminated.IsCompleted)
                    await asker.Terminate();
            }
        }
    }
}
