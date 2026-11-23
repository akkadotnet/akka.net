//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundBatchEncodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// End-to-end behaviour of the ordinary outbound stream when its encode stage hands several
    /// frames downstream per element (<see cref="ArteryBatchEncodeStage"/>): bursts much larger
    /// than one batch must arrive complete and in send order, across a failed connect, under
    /// receiver backpressure, and the sender must still shut down cleanly mid-burst.
    /// </summary>
    public class ArteryOutboundBatchEncodeSpec : AkkaSpec
    {
        public ArteryOutboundBatchEncodeSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int outboundLanes, int port = 0, string extra = "") => ConfigurationFactory.ParseString($"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {port}
            akka.remote.artery.advanced.outbound-lanes = {outboundLanes}
            {extra}
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private sealed class ForwardToProbe : ReceiveActor
        {
            public ForwardToProbe(IActorRef target) => ReceiveAny(msg => target.Tell(msg));
        }

        private sealed class ReportSender : ReceiveActor
        {
            public ReportSender(IActorRef target) => ReceiveAny(_ => target.Tell(Sender));
        }

        private sealed class Ignore : ReceiveActor
        {
            public Ignore() => ReceiveAny(_ => { });
        }

        private static async Task<int[]> ReceiveSequenceAsync(TestProbe probe, int count, TimeSpan timeout) =>
            (await probe.ReceiveNAsync(count, timeout).ToListAsync()).Select(m => m switch
            {
                int i => i,
                GatedPayload g => g.Seq,
                _ => throw new InvalidOperationException($"Unexpected message [{m}]")
            }).ToArray();

        [Theory(DisplayName = "A burst far larger than one batch arrives complete and in send order")]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Should_deliver_burst_in_order(int lanes)
        {
            const int burst = 2000;
            var systemA = ActorSystem.Create("BatchEncodeOrderA", ArteryConfig(lanes));
            var systemB = ActorSystem.Create("BatchEncodeOrderB", ArteryConfig(lanes));
            try
            {
                var probe = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new ForwardToProbe(probe.Ref)), "sink");
                var sink = await systemA.ActorSelection(SelectionPath(systemB, "sink")).ResolveOne(10.Seconds());

                for (var i = 0; i < burst; i++)
                    sink.Tell(i, ActorRefs.NoSender);

                (await ReceiveSequenceAsync(probe, burst, 30.Seconds())).Should().Equal(Enumerable.Range(0, burst));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        [Fact(DisplayName = "A burst over the ordinary stream carries the sender ref id, so the receiver builds each sender from its id")]
        public async Task Should_write_sender_ref_ids_for_burst()
        {
            var systemA = ActorSystem.Create("BatchEncodeIdsA", ArteryConfig(1));
            var systemB = ActorSystem.Create("BatchEncodeIdsB", ArteryConfig(1));
            try
            {
                var probe = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new ReportSender(probe.Ref)), "sink");
                var sink = await systemA.ActorSelection(SelectionPath(systemB, "sink")).ResolveOne(10.Seconds());
                var sender = systemA.ActorOf(Props.Create(() => new Ignore()), "origin");

                for (var i = 0; i < 100; i++)
                    sink.Tell(i, sender);

                var senders = await probe.ReceiveNAsync(100, 30.Seconds()).ToListAsync();
                senders.Should().OnlyContain(s => s is RemoteActorRef && ((RemoteActorRef)s).WireId != 0);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        [Theory(DisplayName = "A burst queued while the connection fails arrives complete and in order once it is re-established")]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Should_deliver_burst_in_order_across_failed_connect(int lanes)
        {
            const int burst = 1000;
            const string systemBName = "BatchEncodeReconnectB";

            // Bind a port, then free it: the first connect attempts hit a dead port, and the peer
            // later comes back on the same port while the inner connection restart backs off.
            var firstIncarnation = ActorSystem.Create(systemBName, ArteryConfig(lanes));
            var port = BoundPort(firstIncarnation);
            await firstIncarnation.Terminate().AwaitWithTimeout(15.Seconds());

            var systemA = ActorSystem.Create("BatchEncodeReconnectA", ArteryConfig(lanes));
            ActorSystem? systemB = null;
            try
            {
                var target = RARP.For(systemA).Provider.ResolveActorRef($"akka://{systemBName}@127.0.0.1:{port}/user/sink");
                for (var i = 0; i < burst; i++)
                    target.Tell(i, ActorRefs.NoSender);

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        systemB = ActorSystem.Create(systemBName, ArteryConfig(lanes, port));
                        break;
                    }
                    catch (Exception) when (attempt < 3)
                    {
                        await Task.Delay(500.Milliseconds());
                    }
                }

                var probe = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new ForwardToProbe(probe.Ref)), "sink");

                (await ReceiveSequenceAsync(probe, burst, 45.Seconds())).Should().Equal(Enumerable.Range(0, burst));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        [Fact(DisplayName = "Terminating the sending system mid-burst completes without errors")]
        public async Task Should_terminate_cleanly_mid_burst()
        {
            var systemA = ActorSystem.Create("BatchEncodeShutdownA", ArteryConfig(4));
            var systemB = ActorSystem.Create("BatchEncodeShutdownB", ArteryConfig(4));
            try
            {
                systemB.ActorOf(Props.Create(() => new Ignore()), "sink");
                var sink = await systemA.ActorSelection(SelectionPath(systemB, "sink")).ResolveOne(10.Seconds());

                var errors = CreateTestProbe();
                systemA.EventStream.Subscribe(errors.Ref, typeof(Error));

                for (var i = 0; i < 3000; i++)
                    sink.Tell(i, ActorRefs.NoSender);

                var terminate = systemA.Terminate();
                (await terminate.AwaitWithTimeout(20.Seconds())).Should().BeTrue("shutdown must not hang mid-burst");
                terminate.IsCompletedSuccessfully.Should().BeTrue();
                await errors.ExpectNoMsgAsync(500.Milliseconds());
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        [Fact(DisplayName = "A burst held back by a stalled receiver arrives complete and in order once the receiver resumes")]
        public async Task Should_deliver_burst_in_order_under_backpressure()
        {
            const int burst = 10000;
            const int lanes = 4;
            var config = ArteryConfig(lanes, extra: $$"""
                akka.remote.artery.advanced.outbound-message-queue-size = 20000
                akka.actor.serializers.gated = "{{typeof(GatedPayloadSerializer).AssemblyQualifiedName}}"
                akka.actor.serialization-bindings { "{{typeof(GatedPayload).AssemblyQualifiedName}}" = gated }
                """);

            GatedPayloadSerializer.Reset();
            var systemA = ActorSystem.Create("BatchEncodeBackpressureA", config);
            var systemB = ActorSystem.Create("BatchEncodeBackpressureB", config);
            try
            {
                var probe = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new ForwardToProbe(probe.Ref)), "sink");
                var sink = await systemA.ActorSelection(SelectionPath(systemB, "sink")).ResolveOne(10.Seconds());

                // The receiver blocks inside deserialization of the first payload, so the inbound
                // stream stops reading and the sender's outbound stream backs up behind TCP.
                var payload = new byte[4 * 1024];
                for (var i = 0; i < burst; i++)
                    sink.Tell(new GatedPayload(i, payload), ActorRefs.NoSender);

                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry
                    .AssociationFor(RARP.For(systemB).Provider.DefaultAddress);
                var lane = Association.SelectLane(sink.Path.Uid, lanes);

                await AwaitConditionAsync(() => Task.FromResult(GatedPayloadSerializer.Entered), 10.Seconds());
                await AwaitAssertAsync(async () =>
                {
                    var before = association.LaneQueueCount(lane);
                    await Task.Delay(200.Milliseconds());
                    association.LaneQueueCount(lane).Should().Be(before).And.BeGreaterThan(0, "the sender must be backpressured");
                }, 10.Seconds());

                GatedPayloadSerializer.Open();

                (await ReceiveSequenceAsync(probe, burst, 60.Seconds())).Should().Equal(Enumerable.Range(0, burst));
            }
            finally
            {
                GatedPayloadSerializer.Open();
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        public sealed record GatedPayload(int Seq, byte[] Data);

        /// <summary>Blocks every deserialization until <see cref="Open"/> is called.</summary>
        public sealed class GatedPayloadSerializer : SerializerWithStringManifest
        {
            private static ManualResetEventSlim _gate = new(false);
            private static volatile bool _entered;

            public GatedPayloadSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public static bool Entered => _entered;

            public static void Reset()
            {
                _gate = new ManualResetEventSlim(false);
                _entered = false;
            }

            public static void Open() => _gate.Set();

            public override int Identifier => 918274;

            public override string Manifest(object o) => ((GatedPayload)o).Seq.ToString();

            public override byte[] ToBinary(object obj) => ((GatedPayload)obj).Data;

            public override object FromBinary(byte[] bytes, string manifest)
            {
                _entered = true;
                if (!_gate.Wait(TimeSpan.FromSeconds(60)))
                    throw new TimeoutException("GatedPayloadSerializer gate was never opened.");
                return new GatedPayload(int.Parse(manifest), bytes);
            }
        }
    }
}
