//-----------------------------------------------------------------------
// <copyright file="ArteryFrameParser.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Incremental, allocation-conscious parser for the Artery TCP per-frame wire format
    /// described in <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire
    /// layout" / Decision 3): <c>[ frame length u32 LE ][ envelope ]</c>. The length field
    /// stores the length of the body that FOLLOWS it — it excludes its own 4 bytes (the
    /// same convention as <c>Akka.Streams.Dsl.Framing</c>'s <c>LengthField</c> stage, i.e.
    /// total frame size on the wire = parsed length + <see cref="LengthFieldSize"/>).
    ///
    /// <para>
    /// This type owns no socket or stream; the caller feeds it raw bytes via
    /// <see cref="Append"/> as they arrive (in any chunking — including one byte at a time)
    /// and pulls completed frame bodies via <see cref="TryReadFrame"/>. It is intended to be
    /// wrapped by a GraphStage in a later Artery task.
    /// </para>
    ///
    /// <para>
    /// <b>Buffering strategy.</b> Appended chunks are held, unmodified, in an internal
    /// queue. Chunks are never copied to assemble a frame body: <see cref="TryReadFrame"/>
    /// builds a (possibly multi-segment) <see cref="ReadOnlySequence{T}"/> directly over
    /// slices of the buffered <see cref="ReadOnlyMemory{T}"/> chunks, using a private
    /// <see cref="ReadOnlySequenceSegment{T}"/> chain. The only copy that ever happens is a
    /// small stack-allocated copy of the 4-byte length field when it straddles a chunk
    /// boundary (needed to read it as a contiguous <c>uint</c>). Once a chunk has been fully
    /// consumed by a returned frame (or the length field), it is dequeued and no longer
    /// referenced by this parser, so it becomes eligible for garbage collection — buffered
    /// memory does not grow without bound as frames are read.
    /// </para>
    ///
    /// <para>
    /// <b>Zero-length bodies are legal.</b> A frame with a declared length of <c>0</c> is a
    /// valid, empty-body frame; <see cref="TryReadFrame"/> yields
    /// <see cref="ReadOnlySequence{T}.Empty"/> for it rather than treating it as an error or
    /// as "no frame available".
    /// </para>
    ///
    /// <para>
    /// This type is not thread-safe. It is designed for a single reader driving both
    /// <see cref="Append"/> and <see cref="TryReadFrame"/> (e.g. one connection's inbound
    /// stream stage).
    /// </para>
    /// </summary>
    internal sealed class ArteryFrameParser
    {
        /// <summary>Size, in bytes, of the little-endian frame-length field that precedes every frame body.</summary>
        public const int LengthFieldSize = 4;

        /// <summary>
        /// Hard upper bound on <c>maxFrameLength</c>: <c>0x00FF_FFFF</c> (16 MiB - 1). This
        /// protects the Artery envelope's 24-bit literal-offset tag space (see
        /// design.md, "Actor-ref / manifest compression" and the envelope TAG encoding) —
        /// literal offsets must stay below <c>0x00FF_FFFF</c>, so no frame body may reach or
        /// exceed it.
        /// </summary>
        public const int MaxAllowedFrameLength = 0x00FF_FFFF;

        private readonly int _maxFrameLength;

        /// <summary>Not-yet-fully-consumed appended chunks, in arrival order.</summary>
        private readonly Queue<ReadOnlyMemory<byte>> _pending = new();

        /// <summary>Number of bytes at the start of <see cref="_pending"/>'s head chunk already yielded/consumed.</summary>
        private int _headOffset;

        /// <summary>Total unconsumed bytes currently buffered across all of <see cref="_pending"/>.</summary>
        private long _bufferedLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryFrameParser"/> class.
        /// </summary>
        /// <param name="maxFrameLength">
        /// The maximum allowed frame BODY length, in bytes (excluding the 4-byte length
        /// field). Must be greater than zero and no greater than
        /// <see cref="MaxAllowedFrameLength"/>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxFrameLength"/> is not in <c>(0, MaxAllowedFrameLength]</c>.
        /// </exception>
        public ArteryFrameParser(int maxFrameLength)
        {
            if (maxFrameLength <= 0 || maxFrameLength > MaxAllowedFrameLength)
                throw new ArgumentOutOfRangeException(
                    nameof(maxFrameLength),
                    maxFrameLength,
                    $"maxFrameLength must be greater than 0 and no greater than {MaxAllowedFrameLength} " +
                    "(0x00FFFFFF) so that Artery envelope literal offsets stay within their 24-bit tag space.");

            _maxFrameLength = maxFrameLength;
        }

        /// <summary>The maximum allowed frame body length configured for this parser.</summary>
        public int MaxFrameLength => _maxFrameLength;

        /// <summary>
        /// Appends a chunk of bytes read from the connection. The chunk is retained by
        /// reference (not copied) until it has been fully consumed by
        /// <see cref="TryReadFrame"/>; callers must not mutate the underlying memory after
        /// appending it.
        /// </summary>
        /// <param name="chunk">The bytes to append. Empty chunks are ignored.</param>
        public void Append(ReadOnlyMemory<byte> chunk)
        {
            if (chunk.IsEmpty)
                return;

            _pending.Enqueue(chunk);
            _bufferedLength += chunk.Length;
        }

        /// <summary>
        /// Attempts to read the next complete frame body from the buffered input.
        /// </summary>
        /// <param name="frame">
        /// On success, the frame body — WITHOUT the 4-byte length prefix. May span multiple
        /// appended chunks, in which case <see cref="ReadOnlySequence{T}.IsSingleSegment"/>
        /// is <see langword="false"/>. On failure (incomplete data), <see cref="ReadOnlySequence{T}.Empty"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if a complete frame was available and is returned in
        /// <paramref name="frame"/>; <see langword="false"/> if more data is needed.
        /// </returns>
        /// <exception cref="ArteryFramingException">
        /// The declared frame length exceeds <see cref="MaxFrameLength"/>.
        /// </exception>
        public bool TryReadFrame(out ReadOnlySequence<byte> frame)
        {
            frame = ReadOnlySequence<byte>.Empty;

            if (!TryPeek(LengthFieldSize, out var lengthSequence))
                return false;

            Span<byte> lengthBytes = stackalloc byte[LengthFieldSize];
            lengthSequence.CopyTo(lengthBytes);
            var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

            if (declaredLength > (uint)_maxFrameLength)
                throw new ArteryFramingException(
                    $"Artery frame declared a body length of {declaredLength} bytes, which exceeds " +
                    $"the configured maximum of {_maxFrameLength} bytes.");

            var bodyLength = (int)declaredLength;

            if (_bufferedLength < LengthFieldSize + (long)bodyLength)
                return false;

            Advance(LengthFieldSize);

            if (bodyLength == 0)
            {
                frame = ReadOnlySequence<byte>.Empty;
                return true;
            }

            var found = TryPeek(bodyLength, out frame);
            Debug.Assert(found, "Body bytes were already confirmed buffered above.");
            Advance(bodyLength);
            return true;
        }

        /// <summary>
        /// Writes a little-endian frame-length field for a body of <paramref name="bodyLength"/>
        /// bytes into <paramref name="destination"/>. This is the one-shot encode-side
        /// counterpart to the length field consumed by <see cref="TryReadFrame"/>.
        /// </summary>
        /// <param name="destination">
        /// The destination span. Must be at least <see cref="LengthFieldSize"/> bytes long.
        /// </param>
        /// <param name="bodyLength">
        /// The length, in bytes, of the frame body that will follow the length field. Must
        /// be non-negative.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="bodyLength"/> is negative, or <paramref name="destination"/> is
        /// shorter than <see cref="LengthFieldSize"/>.
        /// </exception>
        public static void WriteFrameLength(Span<byte> destination, int bodyLength)
        {
            if (bodyLength < 0)
                throw new ArgumentOutOfRangeException(nameof(bodyLength), bodyLength, "Body length must be non-negative.");

            if (destination.Length < LengthFieldSize)
                throw new ArgumentOutOfRangeException(
                    nameof(destination),
                    destination.Length,
                    $"Destination span must be at least {LengthFieldSize} bytes long to hold the frame length field.");

            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)bodyLength);
        }

        /// <summary>
        /// Builds a (possibly multi-segment) view of the next <paramref name="count"/>
        /// buffered bytes WITHOUT consuming them, or returns <see langword="false"/> if
        /// fewer than <paramref name="count"/> bytes are currently buffered.
        /// </summary>
        private bool TryPeek(int count, out ReadOnlySequence<byte> sequence)
        {
            if (_bufferedLength < count)
            {
                sequence = ReadOnlySequence<byte>.Empty;
                return false;
            }

            if (count == 0)
            {
                sequence = ReadOnlySequence<byte>.Empty;
                return true;
            }

            // Single-segment fast path: the requested bytes fit inside the head chunk — the common
            // case when a whole frame arrives in one socket read. Avoids allocating a FrameSegment
            // chain per frame on what becomes the serial decode-island hot path (design.md Decision 2).
            var headChunk = _pending.Peek();
            if (headChunk.Length - _headOffset >= count)
            {
                sequence = new ReadOnlySequence<byte>(headChunk.Slice(_headOffset, count));
                return true;
            }

            FrameSegment? head = null;
            FrameSegment? tail = null;
            var remaining = count;
            var isHeadChunk = true;

            foreach (var chunk in _pending)
            {
                if (remaining == 0)
                    break;

                var offset = isHeadChunk ? _headOffset : 0;
                isHeadChunk = false;

                var available = chunk.Length - offset;
                var take = Math.Min(available, remaining);
                var slice = chunk.Slice(offset, take);

                if (head is null)
                {
                    head = new FrameSegment(slice);
                    tail = head;
                }
                else
                {
                    tail = tail!.Append(slice);
                }

                remaining -= take;
            }

            sequence = new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
            return true;
        }

        /// <summary>Consumes (dequeues/offsets past) <paramref name="count"/> buffered bytes.</summary>
        private void Advance(int count)
        {
            _bufferedLength -= count;

            while (count > 0)
            {
                var chunk = _pending.Peek();
                var available = chunk.Length - _headOffset;

                if (count < available)
                {
                    _headOffset += count;
                    count = 0;
                }
                else
                {
                    count -= available;
                    _pending.Dequeue();
                    _headOffset = 0;
                }
            }
        }

        /// <summary>
        /// Linked-list segment used to compose a multi-segment <see cref="ReadOnlySequence{T}"/>
        /// over slices of buffered chunks without copying the underlying memory.
        /// </summary>
        private sealed class FrameSegment : ReadOnlySequenceSegment<byte>
        {
            public FrameSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public FrameSegment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new FrameSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }
        }
    }
}
