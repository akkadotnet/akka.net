//-----------------------------------------------------------------------
// <copyright file="ArteryFrameCorpus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Pre-serialized frame corpus for the Task 0 substrate benchmarks: the "socket bypassed"
    /// input from tasks.md. <c>messageCount</c> fixed-size frames are encoded into one
    /// contiguous byte stream and then cut into <c>chunkSize</c> chunks that simulate TCP
    /// reads — frames deliberately span chunk boundaries, exactly as real socket reads do.
    /// Recipients are round-robined so every recipient receives exactly
    /// <c>messageCount / recipientCount</c> messages per corpus pass.
    /// </summary>
    public sealed class ArteryFrameCorpus
    {
        private const long OriginUid = 0x1122334455667788L;
        private const int SerializerId = 17;
        private const int ManifestId = 3;
        private const int SenderCount = 32;

        public ReadOnlySequence<byte>[] Chunks { get; }
        public InboundFrame[] DecodedFrames { get; }
        public int MessageCount { get; }
        public int RecipientCount { get; }

        public ArteryFrameCorpus(int messageCount, int recipientCount, int payloadSize, int chunkSize, int seed = 42)
        {
            if (messageCount % recipientCount != 0)
                throw new ArgumentException(
                    $"messageCount ({messageCount}) must divide evenly by recipientCount ({recipientCount}) " +
                    "so per-recipient completion counts are exact.", nameof(messageCount));
            if (payloadSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(payloadSize));

            MessageCount = messageCount;
            RecipientCount = recipientCount;

            var frameTotal = ArteryEnvelopeCodec.FrameTotalLength(payloadSize);
            var stream = new byte[messageCount * frameTotal];
            var rng = new Random(seed);
            var payload = new byte[payloadSize];

            for (var i = 0; i < messageCount; i++)
            {
                rng.NextBytes(payload);
                ArteryEnvelopeCodec.EncodeFrame(
                    stream.AsSpan(i * frameTotal, frameTotal),
                    OriginUid, SerializerId,
                    senderId: i % SenderCount,
                    recipientId: i % recipientCount,
                    manifestId: ManifestId,
                    payload);
            }

            var chunkCount = (stream.Length + chunkSize - 1) / chunkSize;
            Chunks = new ReadOnlySequence<byte>[chunkCount];
            for (var c = 0; c < chunkCount; c++)
            {
                var offset = c * chunkSize;
                var length = Math.Min(chunkSize, stream.Length - offset);
                Chunks[c] = new ReadOnlySequence<byte>(stream, offset, length);
            }

            // Pre-decoded envelopes for the per-message ingress benchmarks (task 0.4),
            // where the element pushed through the source IS the envelope object.
            DecodedFrames = new InboundFrame[messageCount];
            for (var i = 0; i < messageCount; i++)
            {
                var frame = new ReadOnlySequence<byte>(stream, i * frameTotal, frameTotal);
                DecodedFrames[i] = ArteryEnvelopeCodec.Decode(frame);
            }
        }
    }

    /// <summary>
    /// Minimal chained-segment helper used by the hand-written (actor-only) framing path to
    /// append a new chunk onto leftover bytes without copying — the same zero-copy shape
    /// <c>Framing.LengthField</c> uses internally, so config 1 and config 2/3 do equivalent work.
    /// </summary>
    internal sealed class ChainedSegment : ReadOnlySequenceSegment<byte>
    {
        public ChainedSegment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public ChainedSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new ChainedSegment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }

        /// <summary>
        /// Concatenates two sequences into one logical sequence over the same underlying memory.
        /// The left side is expected to be small (a partial trailing frame), so rebuilding its
        /// segment chain is O(1) in practice.
        /// </summary>
        public static ReadOnlySequence<byte> Concat(in ReadOnlySequence<byte> left, in ReadOnlySequence<byte> right)
        {
            if (left.IsEmpty) return right;
            if (right.IsEmpty) return left;

            ChainedSegment? first = null;
            ChainedSegment? last = null;

            foreach (var memory in left)
            {
                if (memory.IsEmpty) continue;
                last = first is null
                    ? first = new ChainedSegment(memory, 0)
                    : last!.Append(memory);
            }

            foreach (var memory in right)
            {
                if (memory.IsEmpty) continue;
                last = first is null
                    ? first = new ChainedSegment(memory, 0)
                    : last!.Append(memory);
            }

            return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
        }
    }
}
