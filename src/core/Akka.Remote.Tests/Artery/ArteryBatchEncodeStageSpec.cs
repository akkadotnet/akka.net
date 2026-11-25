//-----------------------------------------------------------------------
// <copyright file="ArteryBatchEncodeStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stage-level tests for <see cref="ArteryBatchEncodeStage"/>: flushing, the byte cap, and what
    /// it hands over when it stops or fails while holding envelopes.
    /// </summary>
    public class ArteryBatchEncodeStageSpec : AkkaSpec
    {
        private const long OriginUid = 4242;
        private static readonly Address LocalAddress = new("akka", "s", "h", 1);

        public ArteryBatchEncodeStageSpec(ITestOutputHelper output) : base(ConfigurationFactory.ParseString($$"""
            akka.actor.serializers.poison = "{{typeof(PoisonSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings { "{{typeof(Poison).AssemblyQualifiedName}}" = poison }
            """), output)
        {
        }

        [Fact(DisplayName = "The single-frame encoder rejects a null local address at construction")]
        public void Should_RejectNullLocalAddress_When_ConstructingSingleFrameEncoder()
        {
            Assert.Throws<ArgumentNullException>("localAddress", () => new ArteryEncodeStage(Ser, OriginUid, null!));
        }

        [Fact(DisplayName = "The batch encoder rejects a null local address at construction")]
        public void Should_RejectNullLocalAddress_When_ConstructingBatchEncoder()
        {
            Assert.Throws<ArgumentNullException>("localAddress",
                () => new ArteryBatchEncodeStage(Ser, OriginUid, 32, 16 * 1024, null, CarryInto(new()), null!));
        }

        public sealed record Poison(int Seq);

        public sealed class PoisonSerializer : Serializer
        {
            public PoisonSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 918275;
            public override bool IncludeManifest => false;
            public override byte[] ToBinary(object obj) => throw new InvalidOperationException($"poison {((Poison)obj).Seq}");
            public override object FromBinary(byte[] bytes, Type? type) => throw new NotSupportedException();
        }

        /// <summary>Counts outstanding rentals.</summary>
        private sealed class CountingPool : ArrayPool<byte>
        {
            private int _outstanding;
            public int Outstanding => Volatile.Read(ref _outstanding);
            public override byte[] Rent(int minimumLength) { Interlocked.Increment(ref _outstanding); return new byte[Math.Max(16, minimumLength)]; }
            public override void Return(byte[] array, bool clearArray = false) => Interlocked.Decrement(ref _outstanding);
        }

        private Akka.Serialization.Serialization Ser => Sys.Serialization;

        private static IOutboundEnvelope Env(object message) =>
            new OutboundEnvelope(message, "akka://s@h:1/user/sender", "akka://r@h:2/user/recipient", 7, 9);

        private static byte[] Payload(int seq, int size)
        {
            var b = new byte[size];
            BinaryPrimitives.WriteInt32LittleEndian(b, seq);
            return b;
        }

        private byte[] Reference(IOutboundEnvelope e)
        {
            using var w = ArteryEnvelopeCodec.Encode(Ser, OriginUid, e.SenderPath, e.RecipientPath, e.Message, null,
                e.SenderId, e.RecipientId, e.RecipientPathUtf8);
            return w.WrittenSpan.ToArray();
        }

        /// <summary>Splits one element into frames; fails if it does not end on a frame boundary.</summary>
        private static List<byte[]> Frames(ReadOnlySequence<byte> element)
        {
            var bytes = element.ToArray();
            var frames = new List<byte[]>();
            var offset = 0;
            while (offset < bytes.Length)
            {
                (bytes.Length - offset).Should().BeGreaterOrEqualTo(4, "length prefix must not be split");
                var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                (offset + 4 + len).Should().BeLessOrEqualTo(bytes.Length, "frame must not be split across elements");
                frames.Add(bytes.AsSpan(offset, 4 + len).ToArray());
                offset += 4 + len;
            }
            return frames;
        }

        /// <summary>Records what the stage hands over on stop: the envelopes it still held, in order.</summary>
        private static Action<Queue<IOutboundEnvelope>> CarryInto(List<IOutboundEnvelope> carried) =>
            held => { lock (carried) carried.AddRange(held); };

        private (TestPublisher.Probe<IOutboundEnvelope> Pub, TestSubscriber.Probe<ReadOnlySequence<byte>> Sub, List<IOutboundEnvelope> Carried)
            Run(ArrayPool<byte>? pool = null, int maxFrames = 32, long maxBytes = 16 * 1024)
        {
            var carried = new List<IOutboundEnvelope>();
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .Via(new ArteryBatchEncodeStage(Ser, OriginUid, maxFrames, maxBytes, pool, CarryInto(carried), LocalAddress))
                .ToMaterialized(this.SinkProbe<ReadOnlySequence<byte>>(), Keep.Both)
                .Run(Sys.Materializer(ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1)));
            return (pub, sub, carried);
        }

        /// <summary>Sends <paramref name="envelopes"/> while downstream has no demand, so all are held.</summary>
        private static async Task HoldAsync(TestPublisher.Probe<IOutboundEnvelope> pub, IEnumerable<IOutboundEnvelope> envelopes)
        {
            foreach (var e in envelopes)
                await pub.SendNextAsync(e);
            await Task.Delay(300); // let the stage itself grab them
        }

        [Fact(DisplayName = "Held envelopes are flushed in order, exactly once, after upstream completes")]
        public async Task Flush_on_upstream_finish()
        {
            var (pub, sub, carried) = Run();
            var envs = Enumerable.Range(0, 5).Select(i => Env(Payload(i, 64))).ToList();
            await sub.EnsureSubscriptionAsync();
            await HoldAsync(pub, envs);
            await pub.SendCompleteAsync();
            await sub.RequestAsync(10);
            var element = await sub.ExpectNextAsync();
            Frames(element).Should().BeEquivalentTo(envs.Select(Reference), o => o.WithStrictOrdering());
            await sub.ExpectCompleteAsync();
            carried.Should().BeEmpty();
        }

        [Theory(DisplayName = "Byte cap: every element holds only whole frames and the concatenation equals per-message encoding")]
        [InlineData(32, 10)]          // 32 small
        [InlineData(32, 1000)]        // crosses 16 KiB mid-batch
        [InlineData(4, 40000)]        // each frame alone exceeds the element target
        [InlineData(32, 16384 - 200)] // near the cap
        public async Task Byte_cap_never_splits_frames(int count, int payloadSize)
        {
            var (pub, sub, carried) = Run();
            var envs = Enumerable.Range(0, count).Select(i => Env(Payload(i, payloadSize))).ToList();
            await sub.EnsureSubscriptionAsync();
            await HoldAsync(pub, envs);
            await pub.SendCompleteAsync();
            var all = new List<byte[]>();
            while (true)
            {
                await sub.RequestAsync(1);
                var ev = await sub.ExpectEventAsync();
                if (ev is TestSubscriber.OnComplete) break;
                var element = ((TestSubscriber.OnNext<ReadOnlySequence<byte>>)ev).Element;
                var frames = Frames(element);
                if (frames.Count > 1)
                    frames.Take(frames.Count - 1).Sum(f => (long)f.Length).Should().BeLessThan(16 * 1024);
                all.AddRange(frames);
            }
            all.Should().BeEquivalentTo(envs.Select(Reference), o => o.WithStrictOrdering());
            carried.Should().BeEmpty();
        }

        /// <summary>
        /// Message 5 of 12 throws in its serializer while all 12 are held. Envelopes 0..4 were
        /// already encoded into the chain being built when Encode(5) throws.
        /// </summary>
        [Fact(DisplayName = "Mid-batch serializer failure: frames encoded before it are delivered, the envelopes after it handed over, no buffer leaked")]
        public async Task Mid_batch_encode_failure()
        {
            var pool = new CountingPool();
            var (pub, sub, carried) = Run(pool);
            var envs = Enumerable.Range(0, 12).Select(i => i == 5 ? Env(new Poison(i)) : Env(Payload(i, 8))).ToList();
            await sub.EnsureSubscriptionAsync();
            await HoldAsync(pub, envs);
            await sub.RequestAsync(1);
            var element = await sub.ExpectNextAsync();
            var error = await sub.ExpectErrorAsync();
            Output!.WriteLine($"error: {error.GetType().Name}: {error.Message}");
            await AwaitAssertAsync(() => carried.Count.Should().Be(6));
            Output!.WriteLine($"carried over: {carried.Count}; pooled buffers outstanding before release: {pool.Outstanding}");

            // As dev: everything before the poison message goes downstream ahead of the failure, and
            // everything after it is kept for the next materialization. Only the poison one is lost.
            Frames(element).Should().BeEquivalentTo(envs.Take(5).Select(Reference), o => o.WithStrictOrdering());
            carried.Should().Equal(envs.Skip(6));
            pool.Outstanding.Should().Be(1, "only the one buffer holding the frames handed downstream is outstanding");
            element.DisposeOwnedSegments();
            pool.Outstanding.Should().Be(0, "buffers rented for envelopes 0..4 must be returned");
        }

        [Fact(DisplayName = "Kill switch shutdown downstream of the stage while backpressured: held envelopes handed over ahead of the channel")]
        public async Task KillSwitch_trip_while_backpressured()
        {
            var channel = Channel.CreateUnbounded<IOutboundEnvelope>();
            var envs = Enumerable.Range(0, 100).Select(i => Env(Payload(i, 8))).ToList();
            foreach (var e in envs)
                channel.Writer.TryWrite(e);

            var carried = new List<IOutboundEnvelope>();
            var (ks, sub) = ChannelSource.FromReader(channel.Reader)
                .Via(new ArteryBatchEncodeStage(Ser, OriginUid, 32, 16 * 1024, null, CarryInto(carried), LocalAddress))
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<ReadOnlySequence<byte>>(), Keep.Both)
                .Run(Sys.Materializer());

            await sub.EnsureSubscriptionAsync();
            await AwaitAssertAsync(() => channel.Reader.Count.Should().BeLessThan(100));
            await Task.Delay(200);
            ks.Shutdown();
            await sub.ExpectCompleteAsync();
            await AwaitAssertAsync(() => carried.Should().NotBeEmpty());
            Output!.WriteLine($"remainingInChannel={channel.Reader.Count} carried={carried.Count}");

            // Dev's per-message stage takes nothing from the channel without demand; the batch stage
            // may take some, but hands them back in order, so carried-over then channel is all 100.
            var kept = new List<IOutboundEnvelope>(carried);
            while (channel.Reader.TryRead(out var e)) kept.Add(e);
            kept.Should().Equal(envs);
        }
    }
}
