//-----------------------------------------------------------------------
// <copyright file="PooledFrameWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Minimal growable <see cref="IBufferWriter{T}"/> over <see cref="ArrayPool{T}"/>, used by the
    /// V2 single-pass overload of <see cref="ArteryEnvelopeCodec.Encode(Akka.Serialization.Serialization,long,string,string,object)"/>.
    /// The encoded frame's total size is not known up front -- the payload is streamed directly
    /// into this writer via the message's serializer, and the frame-length field plus the fixed
    /// header are back-patched afterward from the actual bytes written (design.md: "back-patched
    /// from bytes-written, not predicted -- no <c>SizeHint</c> dependency").
    ///
    /// <para>
    /// This is deliberately minimal: one rented array that doubles on overflow (copying the
    /// already-written prefix into the new array), plus <see cref="GetPatchSpan"/> for going back
    /// and overwriting already-written bytes at an absolute position. It is not pooled itself --
    /// callers own one instance per encode and must call <see cref="Dispose"/> to return the
    /// rented array.
    /// </para>
    /// </summary>
    internal sealed class PooledFrameWriter : IBufferWriter<byte>, IDisposable
    {
        private const int MinimumCapacity = 64;

        private byte[] _buffer;
        private int _written;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledFrameWriter"/> class.
        /// </summary>
        /// <param name="initialCapacityHint">A hint for the initial rented buffer size; grows automatically if exceeded.</param>
        public PooledFrameWriter(int initialCapacityHint = MinimumCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacityHint, MinimumCapacity));
        }

        /// <summary>The number of bytes written so far.</summary>
        public int WrittenCount => _written;

        /// <summary>A read-only view over the bytes written so far.</summary>
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        /// <summary>A read-only view over the bytes written so far.</summary>
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Advance count must be non-negative.");
            if (_written + count > _buffer.Length)
                throw new InvalidOperationException("Cannot advance past the end of the rented buffer.");

            _written += count;
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        /// <summary>
        /// Returns a mutable view over an already-written absolute byte range, so a caller can
        /// back-patch a field (the frame length, the fixed header) whose value is only known after
        /// later bytes (literals, payload) have been written.
        /// </summary>
        /// <param name="start">The absolute start offset, in bytes, from the start of the writer.</param>
        /// <param name="length">The number of bytes to expose for patching.</param>
        public Span<byte> GetPatchSpan(int start, int length)
        {
            if (start < 0 || length < 0 || start + length > _written)
                throw new ArgumentOutOfRangeException(nameof(start), start, "Patch range must fall entirely within already-written bytes.");

            return _buffer.AsSpan(start, length);
        }

        private void EnsureCapacity(int sizeHint)
        {
            var required = sizeHint <= 0 ? 1 : sizeHint;
            if (_buffer.Length - _written >= required)
                return;

            var newSize = Math.Max(_buffer.Length * 2, _written + required);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _written).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        /// <summary>Returns the rented buffer to <see cref="ArrayPool{T}.Shared"/>.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = Array.Empty<byte>();
            _written = 0;
        }
    }
}
