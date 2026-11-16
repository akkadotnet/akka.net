//-----------------------------------------------------------------------
// <copyright file="ArteryLaneWriteBatchingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for <see cref="ArteryRemoting.AppendFrameToBatch"/> -- the
    /// <c>BatchWeighted</c> aggregate that coalesces already-encoded lane frames ahead of the
    /// ordinary-lanes connection restart boundary. Two contracts are load-bearing and asserted
    /// here directly, without a transport: BYTE IDENTITY (the batched sequence is exactly the
    /// concatenation of the input frames -- the wire sees the same bytes it would have seen
    /// unbatched) and EXACTLY-ONCE OWNERSHIP (after the downstream disposal walk, every source
    /// frame's pooled-buffer owner has been disposed exactly once, and never before it).
    ///
    /// The multi-segment-frame test matters most: production traffic only ever feeds the
    /// aggregate single-segment frames (ArteryEncodeStage output), so the walk over a chained
    /// frame is unexercised at runtime -- this spec is what keeps that path correct.
    /// </summary>
    public class ArteryLaneWriteBatchingSpec : AkkaSpec
    {
        public ArteryLaneWriteBatchingSpec(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// An <see cref="IMemoryOwner{T}"/> that counts <see cref="IDisposable.Dispose"/> calls
        /// instead of returning anything to a pool, so a test can assert exactly-once disposal.
        /// </summary>
        private sealed class TrackingOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _bytes;

            public TrackingOwner(byte[] bytes) => _bytes = bytes;

            public int DisposeCount { get; private set; }

            public Memory<byte> Memory => _bytes;

            public void Dispose() => DisposeCount++;
        }

        /// <summary>Distinct, position-dependent fill so byte-identity failures can't cancel out.</summary>
        private static byte[] Pattern(int length, byte seed)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
                bytes[i] = unchecked((byte)(seed + i));
            return bytes;
        }

        /// <summary>
        /// Builds a frame exactly the way <c>ArteryEncodeStage</c> does: a single-segment,
        /// owner-carrying, segment-backed sequence over the owner's memory.
        /// </summary>
        private static (ReadOnlySequence<byte> Frame, TrackingOwner Owner) EncodeStageStyleFrame(byte[] bytes)
        {
            var owner = new TrackingOwner(bytes);
            return (OwnedSequenceSegment.Create(owner, bytes.Length), owner);
        }

        [Fact(DisplayName = "Should_PreserveBytesAndDisposeEachOwnerExactlyOnce_When_BatchingSingleSegmentFrames")]
        public void Should_PreserveBytesAndDisposeEachOwnerExactlyOnce_When_BatchingSingleSegmentFrames()
        {
            // Varied sizes on purpose: a 3-byte runt, a four-digit frame, a prime-sized straggler.
            var (frame0, owner0) = EncodeStageStyleFrame(Pattern(3, seed: 1));
            var (frame1, owner1) = EncodeStageStyleFrame(Pattern(1000, seed: 101));
            var (frame2, owner2) = EncodeStageStyleFrame(Pattern(17, seed: 201));

            // BatchWeighted's seed is the identity, so the first frame IS the initial batch.
            var batch = frame0;
            batch = ArteryRemoting.AppendFrameToBatch(batch, frame1);
            batch = ArteryRemoting.AppendFrameToBatch(batch, frame2);

            var expected = Pattern(3, seed: 1)
                .Concat(Pattern(1000, seed: 101))
                .Concat(Pattern(17, seed: 201))
                .ToArray();

            batch.Length.Should().Be(expected.Length);
            batch.ToArray().Should().Equal(expected);

            // Batching must never dispose anything itself -- ownership only ever transfers.
            owner0.DisposeCount.Should().Be(0);
            owner1.DisposeCount.Should().Be(0);
            owner2.DisposeCount.Should().Be(0);

            // The downstream disposal walk over the final chain returns every owner exactly once.
            batch.DisposeOwnedSegments();

            owner0.DisposeCount.Should().Be(1);
            owner1.DisposeCount.Should().Be(1);
            owner2.DisposeCount.Should().Be(1);
        }

        [Fact(DisplayName = "Should_PreserveBytesAndDisposeEachOwnerExactlyOnce_When_AppendedFrameHasMultipleSegments")]
        public void Should_PreserveBytesAndDisposeEachOwnerExactlyOnce_When_AppendedFrameHasMultipleSegments()
        {
            var (batchSeedFrame, owner0) = EncodeStageStyleFrame(Pattern(11, seed: 7));

            // Hand-built two-segment frame: first link 5 bytes, second link 900 -- each segment
            // carrying its own owner, chained the same way OwnedSequenceSegment.Append chains.
            var bytesA = Pattern(5, seed: 51);
            var bytesB = Pattern(900, seed: 151);
            var ownerA = new TrackingOwner(bytesA);
            var ownerB = new TrackingOwner(bytesB);
            var segA = new OwnedSequenceSegment(ownerA.Memory, ownerA);
            var segB = segA.Append(ownerB.Memory, ownerB);
            var multiSegmentFrame = new ReadOnlySequence<byte>(segA, 0, segB, bytesB.Length);

            var (trailingFrame, owner3) = EncodeStageStyleFrame(Pattern(29, seed: 251));

            var batch = batchSeedFrame;
            batch = ArteryRemoting.AppendFrameToBatch(batch, multiSegmentFrame);
            // A further append after the multi-segment walk proves the chain's tail stayed valid.
            batch = ArteryRemoting.AppendFrameToBatch(batch, trailingFrame);

            var expected = Pattern(11, seed: 7)
                .Concat(bytesA)
                .Concat(bytesB)
                .Concat(Pattern(29, seed: 251))
                .ToArray();

            batch.Length.Should().Be(expected.Length);
            batch.ToArray().Should().Equal(expected);

            owner0.DisposeCount.Should().Be(0);
            ownerA.DisposeCount.Should().Be(0);
            ownerB.DisposeCount.Should().Be(0);
            owner3.DisposeCount.Should().Be(0);

            batch.DisposeOwnedSegments();

            owner0.DisposeCount.Should().Be(1);
            ownerA.DisposeCount.Should().Be(1);
            ownerB.DisposeCount.Should().Be(1);
            owner3.DisposeCount.Should().Be(1);

            // The walk detached each source segment's owner, so disposing the ORIGINAL frame
            // chain (what a teardown path might still do to the producer's sequence) is a no-op
            // rather than a double-dispose.
            multiSegmentFrame.DisposeOwnedSegments();
            ownerA.DisposeCount.Should().Be(1);
            ownerB.DisposeCount.Should().Be(1);
        }

        [Fact(DisplayName = "Should_DisposeRetainedBatch_When_DownstreamCancels")]
        public async Task Should_DisposeRetainedBatch_When_DownstreamCancels()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var publisher = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var (frame, owner) = EncodeStageStyleFrame(Pattern(128, seed: 3));

            Source.FromPublisher(publisher)
                .Via(Flow.FromGraph(new LaneWriteBatchStage(16 * 1024)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            var subscription = subscriber.EnsureSubscription();
            publisher.SendNext(frame);
            owner.DisposeCount.Should().Be(0);

            subscription.Cancel();

            await AwaitAssertAsync(
                () => owner.DisposeCount.Should().Be(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(20));
        }

        [Fact(DisplayName = "Should_preserve_bytes_and_transfer_owners_When_appending_consolidated_frames")]
        public void Should_preserve_bytes_and_transfer_owners_When_appending_consolidated_frames()
        {
            var pool = new PoisonTrackingPool();
            var first = CompactFrame(pool, Pattern(17, 1));
            var second = CompactFrame(pool, Pattern(31, 2));
            var (third, thirdOwner) = EncodeStageStyleFrame(Pattern(5, 3));
            var batch = ArteryRemoting.AppendFrameToBatch(first.Slice(2, 11), second.Slice(3, 23));
            var head = batch.Start.GetObject();
            batch = ArteryRemoting.AppendFrameToBatch(batch, third);
            Assert.Same(head, batch.Start.GetObject());
            first.DisposeOwnedSegments();
            second.DisposeOwnedSegments();
            third.DisposeOwnedSegments();
            Assert.Equal(0, pool.Returns);
            Assert.Equal(0, thirdOwner.DisposeCount);
            Assert.Equal(Pattern(17, 1).Skip(2).Take(11).Concat(Pattern(31, 2).Skip(3).Take(23)).Concat(Pattern(5, 3)), batch.ToArray());
            batch.DisposeOwnedSegments();
            batch.DisposeOwnedSegments();
            Assert.Equal(2, pool.Returns);
            Assert.Equal(1, thirdOwner.DisposeCount);
        }

        [Fact(DisplayName = "Should_return_partially_transferred_owners_When_normalized_lane_append_fails")]
        public void Should_return_partially_transferred_owners_When_normalized_lane_append_fails()
        {
            var pool = new PoisonTrackingPool();
            var seed = CompactFrame(pool, Pattern(9, 1));
            var frame = CompactFrame(pool, Pattern(7, 2));
            var owner = Assert.IsAssignableFrom<IOwnedSequenceSegment>(frame.Start.GetObject()).DetachOwner()!;
            var head = new OwnedSequenceSegment(owner.Memory, owner);
            var spent = head.Append(new byte[] { 5 }, new TrackingOwner(new byte[] { 5 }));
            spent.DisposeOwner(); // force failure only AFTER the first link has transferred
            var invalid = new ReadOnlySequence<byte>(head, 0, spent, 1);

            Assert.Throws<InvalidOperationException>(() => ArteryRemoting.AppendFrameToBatch(seed, invalid));
            invalid.DisposeOwnedSegments();
            seed.DisposeOwnedSegments();
            frame.DisposeOwnedSegments();
            Assert.Equal(2, pool.Returns);
        }

        [Theory(DisplayName = "Should_return_consolidated_frames_When_retained_lane_batch_is_cancelled_or_failed")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_return_consolidated_frames_When_retained_lane_batch_is_cancelled_or_failed(bool fail)
        {
            var pool = new PoisonTrackingPool();
            var publisher = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            Source.FromPublisher(publisher)
                .Via(Flow.FromGraph(new LaneWriteBatchStage(16 * 1024)))
                .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                .To(Sink.FromSubscriber(subscriber))
                .Run(ActorMaterializer.Create(Sys));
            await subscriber.EnsureSubscriptionAsync();
            // PublisherProbe waits for demand. A third send proves the first two were consumed
            // and normalized even though downstream has never requested the retained batch.
            await publisher.SendNextAsync(CompactFrame(pool, Pattern(9, 1)));
            await publisher.SendNextAsync(CompactFrame(pool, Pattern(7, 2)));
            await publisher.SendNextAsync(CompactFrame(pool, Pattern(5, 3)));
            await publisher.ExpectRequestAsync();
            Assert.Equal(0, pool.Returns);
            if (fail)
                await publisher.SendErrorAsync(new InvalidOperationException("upstream failed"));
            else
                await subscriber.CancelAsync();
            await AwaitAssertAsync(() => Assert.Equal(3, pool.Returns), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(20));
        }

        [Fact(DisplayName = "Should_preserve_borrowed_preamble_and_return_consolidated_rents_When_TCP_coalesces_frames")]
        public async Task Should_preserve_borrowed_preamble_and_return_consolidated_rents_When_TCP_coalesces_frames()
        {
            var pool = new PoisonTrackingPool();
            var preamble = Pattern(16, 71);
            var payloads = Enumerable.Range(0, 40).Select(i => Pattern(101, (byte)i)).ToArray();
            var frames = new[] { new ReadOnlySequence<byte>(preamble) }
                .Concat(payloads.Select(payload => CompactFrame(pool, payload))).ToArray();
            var expected = preamble.Concat(payloads.SelectMany(payload => payload)).ToArray();
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var completion = Source.From(frames)
                .Via(Sys.TcpStream().OutgoingConnection((IPEndPoint)listener.LocalEndpoint))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), ActorMaterializer.Create(Sys));
            using (var socket = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            {
                var actual = new byte[expected.Length];
                await socket.GetStream().ReadExactlyAsync(actual).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(expected, actual);
            }
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            await AwaitAssertAsync(() => Assert.Equal(payloads.Length, pool.Returns), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(20));
            foreach (var frame in frames)
                frame.DisposeOwnedSegments();
            Assert.Equal(payloads.Length, pool.Returns);
            Assert.Equal(Pattern(16, 71), preamble);
        }

        private static ReadOnlySequence<byte> CompactFrame(ArrayPool<byte> pool, byte[] bytes)
        {
            using var writer = new PooledPayloadWriter(pool: pool);
            bytes.CopyTo(writer.GetSpan(bytes.Length));
            writer.Advance(bytes.Length);
            return writer.DetachOwnedSequence();
        }

        private sealed class PoisonTrackingPool : ArrayPool<byte>
        {
            private readonly ConcurrentDictionary<byte[], byte> _outstanding = new();
            private int _returns;
            public int Returns => System.Threading.Volatile.Read(ref _returns);
            public override byte[] Rent(int minimumLength)
            {
                var array = new byte[minimumLength];
                Assert.True(_outstanding.TryAdd(array, 0));
                return array;
            }
            public override void Return(byte[] array, bool clearArray = false)
            {
                Assert.True(_outstanding.TryRemove(array, out _), "Array was returned twice or to the wrong pool.");
                Array.Fill(array, (byte)0xDE);
                System.Threading.Interlocked.Increment(ref _returns);
            }
        }

        /// <summary>
        /// Companion to <see cref="Should_DisposeRetainedBatch_When_DownstreamCancels"/>: the same
        /// retained-batch-on-cancel shape, but asserting the OTHER half of PostStop's job. Freeing
        /// the pooled buffer is memory hygiene; without <c>onDropped</c> that is ALL that happened --
        /// the bytes a producer had already handed to this stage vanished with no
        /// <see cref="Akka.Event.Dropped"/> event and no log at any level, exactly the "silent
        /// discard" shape <see cref="OutboundHandshakeStage.Logic.PostStop"/> used to have before it
        /// started returning its held element through <see cref="IOutboundContext.ReturnUndelivered"/>.
        /// This stage cannot do that -- it sits downstream of encode and the lane merge, so there is
        /// no single <see cref="IOutboundEnvelope"/> left to hand back -- but it can, and after this
        /// fix does, make the loss OBSERVABLE.
        /// </summary>
        [Fact(DisplayName = "Should_ReportBytesDropped_When_DownstreamCancelsWithARetainedBatch")]
        public async Task Should_ReportBytesDropped_When_DownstreamCancelsWithARetainedBatch()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var publisher = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var (frame, owner) = EncodeStageStyleFrame(Pattern(128, seed: 3));

            long? reportedBytes = null;

            Source.FromPublisher(publisher)
                .Via(Flow.FromGraph(new LaneWriteBatchStage(16 * 1024, onDropped: bytes => reportedBytes = bytes)))
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            var subscription = subscriber.EnsureSubscription();
            publisher.SendNext(frame);
            owner.DisposeCount.Should().Be(0);
            reportedBytes.Should().BeNull();

            subscription.Cancel();

            await AwaitAssertAsync(
                () =>
                {
                    owner.DisposeCount.Should().Be(1);
                    reportedBytes.Should().Be(frame.Length);
                },
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(20));
        }
    }
}
