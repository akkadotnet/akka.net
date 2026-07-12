//-----------------------------------------------------------------------
// <copyright file="ArteryLaneWriteBatchingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Linq;
using Akka.IO;
using Akka.Remote.Artery;
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
    public class ArteryLaneWriteBatchingSpec
    {
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
    }
}
