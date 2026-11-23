//-----------------------------------------------------------------------
// <copyright file="ArteryOrdinaryKillSwitchBacklogEndToEndSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
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
    /// PR 9047 review, end to end (compiles on dev and on the PR head): the ordinary outbound
    /// kill switch is tripped (what the control stream does when it detects a dead connection)
    /// while a backlog is queued behind a stalled receiver. Which queued messages still arrive?
    /// </summary>
    public class ArteryOrdinaryKillSwitchBacklogEndToEndSpec : AkkaSpec
    {
        public ArteryOrdinaryKillSwitchBacklogEndToEndSpec(ITestOutputHelper output) : base(output) { }

        public sealed record Held(int Seq, byte[] Data);

        public sealed class HeldSerializer : SerializerWithStringManifest
        {
            internal static ManualResetEventSlim Gate = new(false);
            internal static volatile bool Entered;
            public HeldSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 918279;
            public override string Manifest(object o) => ((Held)o).Seq.ToString();
            public override byte[] ToBinary(object obj) => ((Held)obj).Data;
            public override object FromBinary(byte[] bytes, string manifest)
            {
                Entered = true;
                Gate.Wait(TimeSpan.FromSeconds(60));
                return new Held(int.Parse(manifest), bytes);
            }
        }

        private sealed class ForwardToProbe : ReceiveActor
        {
            public ForwardToProbe(IActorRef target) => ReceiveAny(msg => target.Tell(msg));
        }

        private static Config Cfg(int lanes) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-lanes = {{lanes}}
            akka.remote.artery.advanced.outbound-message-queue-size = 20000
            akka.actor.serializers.held = "{{typeof(HeldSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings { "{{typeof(Held).AssemblyQualifiedName}}" = held }
            """);

        [Theory(DisplayName = "Backlog queued behind a stalled receiver survives a tripped ordinary kill switch")]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Backlog_survives_kill_switch_trip(int lanes)
        {
            const int burst = 3000;
            HeldSerializer.Gate = new ManualResetEventSlim(false);
            HeldSerializer.Entered = false;
            var a = ActorSystem.Create("KsBacklogA", Cfg(lanes));
            var b = ActorSystem.Create("KsBacklogB", Cfg(lanes));
            try
            {
                var probe = CreateTestProbe(b);
                b.ActorOf(Props.Create(() => new ForwardToProbe(probe.Ref)), "sink");
                var bAddress = RARP.For(b).Provider.DefaultAddress;
                var sink = await a.ActorSelection($"akka://KsBacklogB@127.0.0.1:{bAddress.Port}/user/sink").ResolveOne(10.Seconds());

                var droppedProbe = CreateTestProbe(a);
                a.EventStream.Subscribe(droppedProbe.Ref, typeof(Dropped));

                var payload = new byte[4 * 1024];
                for (var i = 0; i < burst; i++) sink.Tell(new Held(i, payload), ActorRefs.NoSender);
                await AwaitConditionAsync(() => Task.FromResult(HeldSerializer.Entered), 10.Seconds());
                await Task.Delay(1000);

                var association = ((ArteryRemoting)RARP.For(a).Provider.Transport).Registry.AssociationFor(bAddress);
                association.TripOutboundKillSwitch();
                await Task.Delay(1000);
                HeldSerializer.Gate.Set();

                var got = new List<int>();
                var deadline = DateTime.UtcNow + 40.Seconds();
                while (DateTime.UtcNow < deadline && got.Count < burst)
                {
                    var m = await probe.ReceiveOneAsync(5.Seconds());
                    if (m is Held h) got.Add(h.Seq);
                    else if (m is null) break;
                }

                var dropped = new List<string>();
                while (await droppedProbe.ReceiveOneAsync(200.Milliseconds()) is Dropped d) dropped.Add(d.Reason);

                var missing = Enumerable.Range(0, burst).Except(got).ToList();
                var ordered = got.Zip(got.Skip(1), (x, y) => x < y).All(x => x);
                Output!.WriteLine($"lanes={lanes} delivered {got.Count}/{burst}; missing {missing.Count} " +
                                  $"[{(missing.Count > 0 ? $"{missing.First()}..{missing.Last()}" : "")}]; ordered={ordered}; " +
                                  $"Dropped events: {string.Join(" | ", dropped)}");
                if (lanes == 1)
                    missing.Should().BeEmpty();
                else
                    // Frames already encoded into the lanes' MergeHub die with the tripped connection,
                    // on dev too. At most lanes x 16 elements (the per-producer buffer) x 4 frames
                    // (a 16 KiB batch holds at most four of this test's 4 KiB frames).
                    missing.Count.Should().BeLessOrEqualTo(256);
            }
            finally
            {
                HeldSerializer.Gate.Set();
                await a.Terminate().AwaitWithTimeout(15.Seconds());
                await b.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }
    }
}
