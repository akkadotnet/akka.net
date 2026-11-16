//-----------------------------------------------------------------------
// <copyright file="PooledSequenceSegment.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Akka.IO
{
    /// <summary>
    /// INTERNAL API. Combines a pooled frame's producer segment and detachable owner. Instances
    /// are never reused: after transfer, the consumer owns this same object until it returns the rent.
    /// Ownership follows the single-threaded handoff contract of <see cref="IOwnedSequenceSegment"/>.
    /// </summary>
    internal sealed class PooledSequenceSegment : ReadOnlySequenceSegment<byte>, IOwnedSequenceSegment, IMemoryOwner<byte>
    {
        private readonly ArrayPool<byte> _pool;
        private bool _hasOwner = true;
        private bool _returned;

        private PooledSequenceSegment(ArrayPool<byte> pool, byte[] array, int length)
        {
            _pool = pool;
            Memory = new ReadOnlyMemory<byte>(array, 0, length);
        }

        internal static ReadOnlySequence<byte> Create(ArrayPool<byte> pool, byte[] array, int length)
        {
            var segment = new PooledSequenceSegment(pool, array, length);
            return new ReadOnlySequence<byte>(segment, 0, segment, length);
        }

        public bool HasOwner => _hasOwner;

        public void DisposeOwner()
        {
            if (!_hasOwner)
                return;

            _hasOwner = false;
            Dispose();
        }

        public IMemoryOwner<byte>? DetachOwner()
        {
            if (!_hasOwner)
                return null;

            _hasOwner = false;
            return this;
        }

        internal OwnedSequenceSegment TransferToAppendableSegment(int start, int length)
        {
            if (!_hasOwner)
                throw new InvalidOperationException("The pooled sequence no longer owns its buffer.");

            // Allocate before transfer so failure cannot strand an owner between the two views.
            var segment = new OwnedSequenceSegment(Memory.Slice(start, length), this);
            _hasOwner = false;
            return segment;
        }

        Memory<byte> IMemoryOwner<byte>.Memory => _returned ? Memory<byte>.Empty : MemoryMarshal.AsMemory(Memory);

        public void Dispose()
        {
            if (_returned)
                return;

            _hasOwner = false;
            _returned = true;
            // This type is constructed only from a rented array. Reuse the base Memory field
            // instead of retaining a second array reference alongside the segment's memory.
            MemoryMarshal.TryGetArray(Memory, out var array);
            _pool.Return(array.Array!);
        }
    }
}
