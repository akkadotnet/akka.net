//-----------------------------------------------------------------------
// <copyright file="PooledPayloadWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;

namespace Akka.Serialization
{
    /// <summary>
    /// A growable, pooled <see cref="IBufferWriter{T}"/> that plugs the gap between the bare
    /// <see cref="IBufferWriter{T}"/> contract and what a zero-copy transport actually needs: the
    /// ability to (a) read back the bytes already written, (b) patch already-written bytes at an
    /// absolute position (reserve-then-patch for length prefixes and headers whose value is only
    /// known once later bytes have been written), and (c) hand off pooled-buffer OWNERSHIP so a
    /// transport can complete an asynchronous socket write and only then return the memory to the
    /// pool, with no intermediate copy.
    ///
    /// <para>
    /// This contract was first invented privately, as Artery G1's internal <c>PooledFrameWriter</c>,
    /// because <see cref="IBufferWriter{T}"/> alone could not express it. It belongs on the core
    /// <see cref="SerializerV2"/> surface: any V2 encode path -- Artery, a future sourcegen'd
    /// serializer, or a user transport -- needs the same reserve/patch/detach shape. Semantically it
    /// plays the same role as Pekko's <c>EnvelopeBufferPool</c>: a reusable wire buffer whose lifetime
    /// is decoupled from the encode call that filled it.
    /// </para>
    ///
    /// <para>
    /// <b>Ownership rules (read this before using the type):</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// A freshly constructed writer owns one array rented from its buffer source: the
    /// <see cref="ArrayPool{T}"/> passed to the constructor, or <see cref="ArrayPool{T}.Shared"/>
    /// when none is given. It grows by renting a bigger array, copying the already-written prefix,
    /// and returning the old array -- never by copying on every write. EVERY rent and return in the
    /// writer's lifetime -- the initial rent, growth, <see cref="Dispose"/>, and the disposal of the
    /// owner returned by <see cref="Detach"/> -- goes through that same pool.
    /// </description></item>
    /// <item><description>
    /// <see cref="Dispose"/> (without a prior <see cref="Detach"/>) returns the current backing array
    /// to the pool. This is the "I'm done, nobody else needs these bytes" path -- e.g. an encode that
    /// threw partway through.
    /// </description></item>
    /// <item><description>
    /// <see cref="Detach"/> is the "hand the bytes to someone else" path: it returns an
    /// <see cref="IMemoryOwner{T}"/> whose <see cref="IMemoryOwner{T}.Memory"/> is exactly the written
    /// slice, and whose own <see cref="IDisposable.Dispose"/> is what returns the array to the pool
    /// (e.g. once an async socket write against that memory has completed). After <see cref="Detach"/>,
    /// this writer is SPENT: every member except <see cref="Dispose"/> throws
    /// <see cref="ObjectDisposedException"/>, calling <see cref="Detach"/> again throws, and this
    /// writer's own <see cref="Dispose"/> becomes a no-op because ownership already moved.
    /// </description></item>
    /// <item><description>
    /// <see cref="Reset"/> reuses the current rented array for a new encode (no re-rent); it is only
    /// valid while the writer is still alive (neither disposed nor detached).
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Why <see cref="ArrayPool{T}"/> and not a custom buffer-source abstraction (or
    /// <see cref="MemoryPool{T}"/>)?</b> Deliberate: <see cref="ArrayPool{T}"/> is the narrowest
    /// standard type that covers every buffer-source strategy this writer needs -- the shared pool,
    /// a dedicated per-transport pool, and POH-pinned arrays via a custom <see cref="ArrayPool{T}"/>
    /// subclass (artery design.md, Decision 9: POH-pinned buffers only if pinning churn shows up in
    /// measurement). A bespoke interface would duplicate it; <see cref="MemoryPool{T}"/> would force
    /// <see cref="Memory{T}"/>-based internals and give up the raw array the patch/growth paths rely on.
    /// </para>
    /// </summary>
    public sealed class PooledPayloadWriter : IBufferWriter<byte>, IDisposable
    {
        private const int MinimumCapacity = 64;
        private const int DefaultInitialCapacityHint = 256;

        private readonly int _maxCapacity;
        private readonly ArrayPool<byte> _pool;
        private byte[]? _buffer;
        private int _written;
        private bool _detached;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledPayloadWriter"/> class.
        /// </summary>
        /// <param name="initialCapacityHint">
        /// A hint for the initial rented buffer size; the writer grows automatically if exceeded
        /// (bounded by <paramref name="maxCapacity"/>).
        /// </param>
        /// <param name="maxCapacity">
        /// The maximum total number of bytes this writer will ever hold. Any <see cref="GetSpan"/>,
        /// <see cref="GetMemory"/>, or <see cref="Advance"/> call that would push the written count
        /// past this bound throws <see cref="PayloadSizeExceededException"/>. Defaults to
        /// <see cref="int.MaxValue"/> (effectively unbounded).
        /// </param>
        /// <param name="pool">
        /// The buffer source for every rent and return in this writer's lifetime (initial rent,
        /// growth, <see cref="Dispose"/>, and the disposal of the owner returned by
        /// <see cref="Detach"/>). <see langword="null"/> (the default) means
        /// <see cref="ArrayPool{T}.Shared"/>. Pass a dedicated pool -- or a custom
        /// <see cref="ArrayPool{T}"/> subclass over POH-pinned arrays -- to control buffer placement
        /// without changing this type.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCapacity"/> is not positive.</exception>
        public PooledPayloadWriter(int initialCapacityHint = DefaultInitialCapacityHint, int maxCapacity = int.MaxValue, ArrayPool<byte>? pool = null)
        {
            if (maxCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCapacity), maxCapacity, "Max capacity must be positive.");

            _maxCapacity = maxCapacity;
            _pool = pool ?? ArrayPool<byte>.Shared;
            var initial = Math.Min(Math.Max(initialCapacityHint, MinimumCapacity), maxCapacity);
            _buffer = _pool.Rent(initial);
        }

        /// <summary>The number of bytes written so far.</summary>
        public int WrittenCount
        {
            get
            {
                ThrowIfSpent();
                return _written;
            }
        }

        /// <summary>A read-only view over the bytes written so far.</summary>
        public ReadOnlySpan<byte> WrittenSpan
        {
            get
            {
                ThrowIfSpent();
                return _buffer!.AsSpan(0, _written);
            }
        }

        /// <summary>A read-only view over the bytes written so far.</summary>
        public ReadOnlyMemory<byte> WrittenMemory
        {
            get
            {
                ThrowIfSpent();
                return _buffer!.AsMemory(0, _written);
            }
        }

        /// <inheritdoc />
        /// <exception cref="PayloadSizeExceededException">Advancing by <paramref name="count"/> would exceed <c>maxCapacity</c>.</exception>
        public void Advance(int count)
        {
            ThrowIfSpent();
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Advance count must be non-negative.");

            var neededTotal = (long)_written + count;
            if (neededTotal > _maxCapacity)
                throw new PayloadSizeExceededException(neededTotal, _maxCapacity);

            if (_written + count > _buffer!.Length)
                throw new InvalidOperationException("Cannot advance past the end of the rented buffer.");

            _written += count;
        }

        /// <inheritdoc />
        /// <exception cref="PayloadSizeExceededException">Satisfying <paramref name="sizeHint"/> would exceed <c>maxCapacity</c>.</exception>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            ThrowIfSpent();
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint), sizeHint, "Size hint must be non-negative.");

            var required = sizeHint == 0 ? 1 : sizeHint;
            EnsureCapacity(required);

            var memory = _buffer!.AsMemory(_written);
            var remaining = _maxCapacity - _written;
            return memory.Length > remaining ? memory.Slice(0, remaining) : memory;
        }

        /// <inheritdoc />
        /// <exception cref="PayloadSizeExceededException">Satisfying <paramref name="sizeHint"/> would exceed <c>maxCapacity</c>.</exception>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            ThrowIfSpent();
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint), sizeHint, "Size hint must be non-negative.");

            var required = sizeHint == 0 ? 1 : sizeHint;
            EnsureCapacity(required);

            var span = _buffer!.AsSpan(_written);
            var remaining = _maxCapacity - _written;
            return span.Length > remaining ? span.Slice(0, remaining) : span;
        }

        /// <summary>
        /// Returns a mutable view over an already-written absolute byte range, so a caller can
        /// back-patch a field (a frame length, a fixed header) whose value is only known after
        /// later bytes have been written.
        /// </summary>
        /// <param name="start">The absolute start offset, in bytes, from the start of the writer.</param>
        /// <param name="length">The number of bytes to expose for patching.</param>
        /// <exception cref="ArgumentOutOfRangeException">The patch range falls outside the already-written bytes.</exception>
        public Span<byte> GetPatchSpan(int start, int length)
        {
            ThrowIfSpent();
            if (start < 0 || length < 0 || start + length > _written)
                throw new ArgumentOutOfRangeException(nameof(start), start, "Patch range must fall entirely within already-written bytes.");

            return _buffer!.AsSpan(start, length);
        }

        /// <summary>
        /// Detaches ownership of the backing buffer from this writer: returns an
        /// <see cref="IMemoryOwner{T}"/> whose <see cref="IMemoryOwner{T}.Memory"/> is exactly the
        /// written slice. Disposing the returned owner returns the underlying array to the SAME pool
        /// this writer rented it from (the constructor's <c>pool</c>, or <see cref="ArrayPool{T}.Shared"/>).
        /// The <see cref="IMemoryOwner{T}"/> return type deliberately leaves room for a future
        /// pooled-owner implementation (Pekko <c>EnvelopeBufferPool</c>-style) without an API change.
        ///
        /// <para>
        /// After this call the writer is SPENT: every member except <see cref="Dispose"/> throws
        /// <see cref="ObjectDisposedException"/> -- including a second call to <see cref="Detach"/>.
        /// This writer's own <see cref="Dispose"/> becomes a no-op, because ownership of the buffer
        /// has moved to the returned owner.
        /// </para>
        /// </summary>
        /// <returns>An owner of the written bytes; the caller is responsible for disposing it.</returns>
        /// <exception cref="ObjectDisposedException">The writer has already been detached or disposed.</exception>
        public IMemoryOwner<byte> Detach()
        {
            ThrowIfSpent();

            var owner = new RentedMemoryOwner(_pool, _buffer!, _written);
            _buffer = null;
            _detached = true;
            return owner;
        }

        /// <summary>
        /// Resets the writer for reuse without re-renting: the currently rented array is kept and
        /// the written count is set back to zero. Only valid while the writer is still alive (has
        /// not been detached or disposed).
        /// </summary>
        /// <exception cref="ObjectDisposedException">The writer has been detached or disposed.</exception>
        public void Reset()
        {
            ThrowIfSpent();
            _written = 0;
        }

        /// <summary>
        /// Returns the rented buffer to the pool it was rented from. A no-op if this writer has
        /// already been detached (ownership moved to the <see cref="IMemoryOwner{T}"/> returned by
        /// <see cref="Detach"/>) or already disposed.
        /// </summary>
        public void Dispose()
        {
            if (_detached || _disposed)
                return;

            _disposed = true;
            _pool.Return(_buffer!);
            _buffer = null;
            _written = 0;
        }

        private void EnsureCapacity(int required)
        {
            var neededTotal = (long)_written + required;
            if (neededTotal > _maxCapacity)
                throw new PayloadSizeExceededException(neededTotal, _maxCapacity);

            if (_buffer!.Length - _written >= required)
                return;

            var grownSize = Math.Max((long)_buffer.Length * 2, neededTotal);
            var newSize = (int)Math.Min(grownSize, _maxCapacity);

            var newBuffer = _pool.Rent(newSize);
            _buffer.AsSpan(0, _written).CopyTo(newBuffer);
            _pool.Return(_buffer);
            _buffer = newBuffer;
        }

        private void ThrowIfSpent()
        {
            if (_detached)
                throw new ObjectDisposedException(nameof(PooledPayloadWriter),
                    "This writer was detached via Detach(); ownership of its backing buffer moved to the returned IMemoryOwner<byte>. All members except Dispose() are invalid after Detach().");
            if (_disposed)
                throw new ObjectDisposedException(nameof(PooledPayloadWriter));
        }

        /// <summary>
        /// The <see cref="IMemoryOwner{T}"/> returned by <see cref="Detach"/>: owns the rented array
        /// that backed the writer and, on disposal, returns it to the SAME pool the writer rented it
        /// from -- never to a pool the array did not come from.
        /// </summary>
        private sealed class RentedMemoryOwner : IMemoryOwner<byte>
        {
            private readonly ArrayPool<byte> _pool;
            private byte[]? _array;
            private readonly int _length;

            public RentedMemoryOwner(ArrayPool<byte> pool, byte[] array, int length)
            {
                _pool = pool;
                _array = array;
                _length = length;
            }

            public Memory<byte> Memory => _array is null ? Memory<byte>.Empty : new Memory<byte>(_array, 0, _length);

            public void Dispose()
            {
                if (_array is null)
                    return;

                _pool.Return(_array);
                _array = null;
            }
        }
    }
}
