//-----------------------------------------------------------------------
// <copyright file="OwnedSequenceSegment.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Threading;

namespace Akka.IO
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Implemented by a <see cref="ReadOnlySequenceSegment{T}"/> of <see cref="byte"/> that may be
    /// carrying ownership of the pooled memory it wraps (see <see cref="OwnedSequenceSegment"/>).
    /// Consumers that walk a <see cref="ReadOnlySequence{T}"/> of bytes (e.g. <c>TcpConnection</c>'s
    /// disposal matrix) recognize an owner-carrying segment through this interface rather than the
    /// concrete <see cref="OwnedSequenceSegment"/> type, so the walk works for any segment
    /// implementation that chooses to opt in.
    /// </summary>
    internal interface IOwnedSequenceSegment
    {
        /// <summary>
        /// <see langword="true"/> if this segment currently carries a live (not yet disposed)
        /// owner. Does not mutate or dispose anything — used by callers that need to decide
        /// something (e.g. "should I take a defensive copy?") based on whether ANY segment in a
        /// chain carries an owner, without triggering disposal as a side effect.
        /// </summary>
        bool HasOwner { get; }

        /// <summary>
        /// Disposes the owner carried by this segment, if any. MUST be idempotent: calling this
        /// more than once (e.g. from two different teardown paths racing to be conservative) is
        /// harmless and disposes the owner at most once.
        /// </summary>
        void DisposeOwner();
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// A <see cref="ReadOnlySequenceSegment{T}"/> of <see cref="byte"/> that optionally carries an
    /// <see cref="IMemoryOwner{T}"/> alongside the memory it exposes. This is the mechanism behind
    /// "ownership-carrying <c>Tcp.Write</c>" (modernize-akka-io-tcp design.md, Decision 8 and its
    /// 2026-07-07 mechanism refinement): a producer (e.g. Artery's encode stage) mints one of these
    /// per pooled frame it hands to Akka.IO, and <c>TcpConnection</c> disposes the owner at the exact
    /// point the bytes have been copied into the output pipe (or, on a non-success path, at the
    /// point it is certain the bytes will never be read).
    ///
    /// <para>
    /// <b>Why a segment and not a <c>Tcp.Write</c> field.</b> The owner has to ride from wherever it
    /// is minted, through any pass-through stages, to wherever the pipe copy happens — and the only
    /// thing that flows unchanged across those stages is <see cref="Tcp.Write.Data"/>'s
    /// <see cref="ReadOnlySequence{T}"/> itself. Carrying the owner in the segment means zero-copy
    /// coalescing (chaining N frames into one <c>Tcp.Write</c>) carries all N owners for free, with
    /// no change to the public <c>Tcp.Write</c>/<c>SimpleWriteCommand</c> surface.
    /// </para>
    ///
    /// <para>
    /// <b>Construction gotcha.</b> A single-segment, owner-carrying <see cref="ReadOnlySequence{T}"/>
    /// MUST be built segment-backed (<c>new ReadOnlySequence&lt;byte&gt;(segment, 0, segment, length)</c>)
    /// and not memory-backed (<c>new ReadOnlySequence&lt;byte&gt;(memory)</c>) — the latter does not
    /// expose the segment via <c>Data.Start.GetObject()</c>, so a disposal walk would silently see no
    /// segment at all and leak the owner. Use <see cref="Create(IMemoryOwner{byte})"/> or
    /// <see cref="Create(IMemoryOwner{byte}, int)"/> rather than constructing the sequence by hand.
    /// </para>
    ///
    /// <para>
    /// <b>Borrowed segments in a mixed chain.</b> A segment constructed with <c>owner: null</c> is a
    /// legitimate, borrowed (owner-less) link in a chain that also contains owner-carrying segments —
    /// <see cref="DisposeOwner"/> is then simply a no-op for that link. This lets a single
    /// <see cref="ReadOnlySequence{T}"/> mix borrowed and owned memory (e.g. a one-time preamble
    /// prepended to owned frames) without a different segment type.
    /// </para>
    /// </summary>
    internal sealed class OwnedSequenceSegment : ReadOnlySequenceSegment<byte>, IOwnedSequenceSegment
    {
        private IMemoryOwner<byte>? _owner;

        /// <summary>
        /// Creates a segment over <paramref name="memory"/>, optionally carrying <paramref name="owner"/>.
        /// </summary>
        /// <param name="memory">
        /// The memory this segment exposes. When <paramref name="owner"/> is supplied, this MUST be
        /// (a slice of) <paramref name="owner"/>'s own <see cref="IMemoryOwner{T}.Memory"/> — this
        /// type does not verify that relationship.
        /// </param>
        /// <param name="owner">
        /// The owner of the pooled memory backing this segment, or <see langword="null"/> for a
        /// borrowed (owner-less) segment whose <see cref="DisposeOwner"/> is a no-op.
        /// </param>
        public OwnedSequenceSegment(ReadOnlyMemory<byte> memory, IMemoryOwner<byte>? owner = null)
        {
            Memory = memory;
            _owner = owner;
        }

        /// <summary>
        /// Chains a new segment after this one (zero-copy coalescing), returning the new segment so
        /// callers can keep appending. Sets <see cref="ReadOnlySequenceSegment{T}.RunningIndex"/> and
        /// <see cref="ReadOnlySequenceSegment{T}.Next"/> correctly.
        /// </summary>
        /// <param name="memory">The memory the appended segment exposes.</param>
        /// <param name="owner">The owner carried by the appended segment, or <see langword="null"/> for a borrowed link.</param>
        /// <returns>The newly appended segment.</returns>
        public OwnedSequenceSegment Append(ReadOnlyMemory<byte> memory, IMemoryOwner<byte>? owner = null)
        {
            var next = new OwnedSequenceSegment(memory, owner)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }

        /// <inheritdoc />
        public bool HasOwner => Volatile.Read(ref _owner) is not null;

        /// <summary>
        /// Disposes the owner carried by this segment, if any. Idempotent: the owner reference is
        /// atomically cleared before disposal, so a second (or racing) call is a harmless no-op —
        /// this mirrors <c>PooledPayloadWriter.RentedMemoryOwner</c>'s own idempotent
        /// <see cref="IDisposable.Dispose"/>, so overlapping disposal paths degrade to a harmless
        /// double-dispose rather than a use-after-return.
        /// </summary>
        public void DisposeOwner()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Dispose();
        }

        /// <summary>
        /// Builds an owner-carrying, single-segment <see cref="ReadOnlySequence{T}"/> over the whole
        /// of <paramref name="owner"/>'s memory. Segment-backed (see the construction gotcha
        /// documented on this type), so a disposal walk starting at
        /// <c>Data.Start.GetObject()</c> finds this segment.
        /// </summary>
        /// <param name="owner">The owner of the pooled memory to wrap. Ownership transfers to the returned sequence's segment.</param>
        public static ReadOnlySequence<byte> Create(IMemoryOwner<byte> owner)
        {
            if (owner is null)
                throw new ArgumentNullException(nameof(owner));

            return Create(owner, owner.Memory.Length);
        }

        /// <summary>
        /// Builds an owner-carrying, single-segment <see cref="ReadOnlySequence{T}"/> over the first
        /// <paramref name="length"/> bytes of <paramref name="owner"/>'s memory. Segment-backed (see
        /// the construction gotcha documented on this type), so a disposal walk starting at
        /// <c>Data.Start.GetObject()</c> finds this segment.
        /// </summary>
        /// <param name="owner">The owner of the pooled memory to wrap. Ownership transfers to the returned sequence's segment.</param>
        /// <param name="length">The number of bytes, from the start of <paramref name="owner"/>'s memory, to expose.</param>
        public static ReadOnlySequence<byte> Create(IMemoryOwner<byte> owner, int length)
        {
            if (owner is null)
                throw new ArgumentNullException(nameof(owner));
            if ((uint)length > (uint)owner.Memory.Length)
                throw new ArgumentOutOfRangeException(nameof(length), length, "Length must not exceed the owner's memory length.");

            var segment = new OwnedSequenceSegment(owner.Memory.Slice(0, length), owner);
            return new ReadOnlySequence<byte>(segment, 0, segment, length);
        }
    }

    /// <summary>
    /// INTERNAL API. Extension helpers for walking a <see cref="ReadOnlySequence{T}"/> of bytes that
    /// may be backed by a chain of <see cref="IOwnedSequenceSegment"/>-implementing segments.
    /// </summary>
    internal static class OwnedSequenceSegmentExtensions
    {
        /// <summary>
        /// Disposes every owner-carrying segment in <paramref name="data"/>'s backing chain, in
        /// order. A no-op for a memory-/array-backed sequence (e.g. <c>new ReadOnlySequence&lt;byte&gt;(memory)</c>)
        /// or for a chain with no owner-carrying segments — both are the common "borrowed write" case
        /// and must be byte-for-byte unaffected by this call.
        /// </summary>
        /// <param name="data">The sequence to walk. Not mutated by this call.</param>
        internal static void DisposeOwnedSegments(this ReadOnlySequence<byte> data)
        {
            if (data.Start.GetObject() is not ReadOnlySequenceSegment<byte> segment)
                return;

            // Bound the walk to THIS sequence's own end segment: `data` may be a slice of a longer
            // segment chain, and disposing owners past `data.End` would free pooled memory that
            // belongs to a DIFFERENT write still referencing the tail (a use-after-return). No
            // current caller slices an owner-carrying chain this way — Artery's encode stage mints a
            // single segment, and coalescing spans the whole head->tail chain it drained — but
            // bounding here keeps the walk correct for any future caller that does. `data.End`'s
            // segment is disposed inclusively, then the walk stops.
            var end = data.End.GetObject() as ReadOnlySequenceSegment<byte>;

            while (segment is not null)
            {
                if (segment is IOwnedSequenceSegment owned)
                    owned.DisposeOwner();

                if (ReferenceEquals(segment, end))
                    break;

                segment = segment.Next!;
            }
        }

        /// <summary>
        /// <see langword="true"/> if <paramref name="data"/>'s backing chain contains at least one
        /// segment currently carrying a live (not yet disposed) owner. Does not dispose anything —
        /// safe to call as a decision point (e.g. "does this write need a defensive copy, or can it
        /// be queued as-is under the ownership-transfer contract?").
        /// </summary>
        /// <param name="data">The sequence to inspect. Not mutated by this call.</param>
        internal static bool HasOwnedSegments(this ReadOnlySequence<byte> data)
        {
            if (data.Start.GetObject() is not ReadOnlySequenceSegment<byte> segment)
                return false;

            while (segment is not null)
            {
                if (segment is IOwnedSequenceSegment { HasOwner: true })
                    return true;

                segment = segment.Next!;
            }

            return false;
        }
    }
}
