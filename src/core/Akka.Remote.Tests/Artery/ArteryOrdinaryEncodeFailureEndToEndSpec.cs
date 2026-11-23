//-----------------------------------------------------------------------
// <copyright file="ArteryOrdinaryEncodeFailureEndToEndSpec.cs" company="Akka.NET Project">
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
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// PR 9047 review, end to end (compiles on dev and on the PR head): one unserializable
    /// message in a burst sent while the receiver is stalled. Which of the messages queued
    /// AFTER it still arrive?
    /// </summary>
    public class ArteryOrdinaryEncodeFailureEndToEndSpec : AkkaSpec
    {
        public ArteryOrdinaryEncodeFailureEndToEndSpec(ITestOutputHelper output) : base(output) { }

        public sealed record Slow(int Seq, byte[] Data);
        public sealed record Bad(int Seq);

        public sealed class SlowSerializer : SerializerWithStringManifest
        {
            internal static ManualResetEventSlim Gate = new(false);
            internal static volatile bool Entered;
            public SlowSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 918277;
            public override string Manifest(object o) => ((Slow)o).Seq.ToString();
            public override byte[] ToBinary(object obj) => ((Slow)obj).Data;
            public override object FromBinary(byte[] bytes, string manifest)
            {
                Entered = true;
                Gate.Wait(TimeSpan.FromSeconds(60));
                return new Slow(int.Parse(manifest), bytes);
            }
        }

        public sealed class BadSerializer : Serializer
        {
            public BadSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 918278;
            public override bool IncludeManifest => false;
            public override byte[] ToBinary(object obj) => throw new InvalidOperationException("unserializable");
            public override object FromBinary(byte[] bytes, Type? type) => throw new NotSupportedException();
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
            akka.actor.serializers.slow = "{{typeof(SlowSerializer).AssemblyQualifiedName}}"
            akka.actor.serializers.bad = "{{typeof(BadSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings {
              "{{typeof(Slow).AssemblyQualifiedName}}" = slow
              "{{typeof(Bad).AssemblyQualifiedName}}" = bad
            }
            """);

        [Theory(DisplayName = "Messages queued after one unserializable message survive the stream failure")]
        [InlineData(1)]
        [InlineData(4)]
        public async Task Successors_of_unserializable_message_are_delivered(int lanes)
        {
            const int head = 3000, tail = 200;
            SlowSerializer.Gate = new ManualResetEventSlim(false);
            SlowSerializer.Entered = false;
            var a = ActorSystem.Create("EncFailA", Cfg(lanes));
            var b = ActorSystem.Create("EncFailB", Cfg(lanes));
            try
            {
                var probe = CreateTestProbe(b);
                b.ActorOf(Props.Create(() => new ForwardToProbe(probe.Ref)), "sink");
                var port = RARP.For(b).Provider.DefaultAddress.Port;
                var sink = await a.ActorSelection($"akka://EncFailB@127.0.0.1:{port}/user/sink").ResolveOne(10.Seconds());

                var payload = new byte[4 * 1024];
                for (var i = 0; i < head; i++) sink.Tell(new Slow(i, payload), ActorRefs.NoSender);
                await AwaitConditionAsync(() => Task.FromResult(SlowSerializer.Entered), 10.Seconds());
                await Task.Delay(1000); // let every stage upstream of TCP fill up
                sink.Tell(new Bad(-1), ActorRefs.NoSender);
                for (var i = 0; i < tail; i++) sink.Tell(new Slow(100_000 + i, payload), ActorRefs.NoSender);
                await Task.Delay(1000);
                SlowSerializer.Gate.Set();

                var got = new List<int>();
                var deadline = DateTime.UtcNow + 40.Seconds();
                while (DateTime.UtcNow < deadline && !got.Contains(100_000 + tail - 1))
                {
                    var m = await probe.ReceiveOneAsync(5.Seconds());
                    if (m is Slow s) got.Add(s.Seq);
                    else if (m is null) break;
                }
                // drain stragglers
                while (await probe.ReceiveOneAsync(1.Seconds()) is Slow s2) got.Add(s2.Seq);

                var headGot = got.Count(x => x < 100_000);
                var tailGot = got.Where(x => x >= 100_000).ToList();
                Output!.WriteLine($"lanes={lanes} head delivered {headGot}/{head}; tail delivered {tailGot.Count}/{tail}; " +
                                 $"first tail seqs: {string.Join(",", tailGot.Take(5))}");
                got.Should().BeInAscendingOrder("per-pair ordering must hold");
                tailGot.Should().Equal(Enumerable.Range(100_000, tail), "messages queued after the bad message must still arrive");
            }
            finally
            {
                SlowSerializer.Gate.Set();
                await a.Terminate().AwaitWithTimeout(15.Seconds());
                await b.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }
    }
}
