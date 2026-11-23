//-----------------------------------------------------------------------
// <copyright file="ArteryPairLaneSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// With four inbound and four outbound lanes, messages that carry both a sender and a recipient
    /// id are laned by the pair: many senders to one recipient spread across lanes, and each
    /// sender's messages to each recipient still arrive in send order.
    /// </summary>
    public class ArteryPairLaneSpec : AkkaSpec
    {
        private const int Senders = 8;
        private const int Recipients = 4;
        private const int PerPair = 500;

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        public ArteryPairLaneSpec(ITestOutputHelper output) : base(LaneConfig, output)
        {
        }

        private static Config LaneConfig => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = 4
            akka.remote.artery.advanced.outbound-lanes = 4
            akka.remote.artery.advanced.outbound-message-queue-size = 32768
            """);

        private sealed record Start(IReadOnlyList<IActorRef> Recipients);

        private sealed class SequenceSender : ReceiveActor
        {
            public SequenceSender() => Receive<Start>(start =>
            {
                for (var seq = 0; seq < PerPair; seq++)
                    foreach (var recipient in start.Recipients)
                        recipient.Tell(seq.ToString(), Self);
            });
        }

        /// <summary>Checks that each sender's numbers arrive in order and reports once all have arrived.</summary>
        private sealed class OrderChecker : ReceiveActor
        {
            private readonly Dictionary<string, int> _next = new();
            private int _received;
            private int _outOfOrder;

            public OrderChecker(IActorRef report) => Receive<string>(m =>
            {
                var from = Sender.Path.Name;
                var expected = _next.TryGetValue(from, out var n) ? n : 0;
                if (int.Parse(m) != expected)
                    _outOfOrder++;
                _next[from] = expected + 1;

                if (++_received == Senders * PerPair)
                    report.Tell($"{Self.Path.Name}: senders={_next.Count} received={_received} out-of-order={_outOfOrder}");
            });
        }

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        [Fact(DisplayName = "At four lanes, each sender's messages to each recipient arrive in send order while many senders share the recipients")]
        public async Task Should_preserve_order_per_sender_recipient_pair()
        {
            var a = ActorSystem.Create("pair-lanes-a", LaneConfig);
            var b = ActorSystem.Create("pair-lanes-b", LaneConfig);
            try
            {
                var report = CreateTestProbe(a);
                var reportOnB = await b.ActorSelection(report.Ref.Path.ToStringWithAddress(AddressOf(a))).ResolveOne(Timeout);

                var recipients = new List<IActorRef>();
                for (var r = 0; r < Recipients; r++)
                {
                    b.ActorOf(Props.Create(() => new OrderChecker(reportOnB)), $"checker-{r}");
                    recipients.Add(await a.ActorSelection(new RootActorPath(AddressOf(b)) / "user" / $"checker-{r}").ResolveOne(Timeout));
                }

                var senders = Enumerable.Range(0, Senders)
                    .Select(s => a.ActorOf(Props.Create(() => new SequenceSender()), $"sender-{s}"))
                    .ToList();
                foreach (var sender in senders)
                    sender.Tell(new Start(recipients));

                var reports = new List<string>();
                for (var r = 0; r < Recipients; r++)
                    reports.Add(await report.ExpectMsgAsync<string>(Timeout));

                reports.Should().BeEquivalentTo(Enumerable.Range(0, Recipients)
                    .Select(r => $"checker-{r}: senders={Senders} received={Senders * PerPair} out-of-order=0"));
            }
            finally
            {
                await Task.WhenAll(a.Terminate(), b.Terminate());
            }
        }
    }
}
