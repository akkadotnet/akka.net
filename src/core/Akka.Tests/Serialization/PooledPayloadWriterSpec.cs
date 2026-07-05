//-----------------------------------------------------------------------
// <copyright file="PooledPayloadWriterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Akka.Serialization;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Unit tests for <see cref="PooledPayloadWriter"/>: the pooled, growable
    /// <see cref="IBufferWriter{T}"/> that backs the V2 single-pass Artery encode path (and any future
    /// V2 encode path needing reserve/patch/detach semantics). Covers growth-preserves-bytes, the
    /// patch round trip, <see cref="PooledPayloadWriter.Detach"/> ownership transfer, <c>maxCapacity</c>
    /// enforcement (both the <see cref="PooledPayloadWriter.GetSpan"/> and
    /// <see cref="PooledPayloadWriter.Advance"/> paths), and <see cref="PooledPayloadWriter.Reset"/> reuse.
    /// </summary>
    public class PooledPayloadWriterSpec
    {
        // ===================== growth =====================

        [Fact(DisplayName = "Should_preserve_already_written_bytes_When_growing_across_multiple_re_rents")]
        public void Should_preserve_already_written_bytes_When_growing_across_multiple_re_rents()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8);
            var expected = new List<byte>();
            var rnd = new Random(42);

            for (var i = 0; i < 50; i++)
            {
                var chunkLength = rnd.Next(1, 37); // varying sizes to force several regrows past the tiny 8-byte initial capacity
                var chunk = new byte[chunkLength];
                rnd.NextBytes(chunk);

                chunk.CopyTo(writer.GetSpan(chunkLength));
                writer.Advance(chunkLength);
                expected.AddRange(chunk);
            }

            Assert.Equal(expected.Count, writer.WrittenCount);
            Assert.Equal(expected.ToArray(), writer.WrittenSpan.ToArray());
            Assert.Equal(expected.ToArray(), writer.WrittenMemory.ToArray());
        }

        // ===================== patch round trip =====================

        [Fact(DisplayName = "Should_round_trip_a_back_patched_length_prefix_When_using_GetPatchSpan")]
        public void Should_round_trip_a_back_patched_length_prefix_When_using_GetPatchSpan()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 16);

            // Reserve a 4-byte length prefix -- its value is only known once the payload is written.
            writer.GetSpan(4);
            writer.Advance(4);

            var payload = MakeBytes(23, seed: 7);
            payload.CopyTo(writer.GetSpan(payload.Length));
            writer.Advance(payload.Length);

            BinaryPrimitives.WriteUInt32LittleEndian(writer.GetPatchSpan(0, 4), (uint)payload.Length);

            Assert.Equal(4 + payload.Length, writer.WrittenCount);
            var written = writer.WrittenSpan.ToArray();
            Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32LittleEndian(written));
            Assert.Equal(payload, written.AsSpan(4).ToArray());
        }

        [Fact(DisplayName = "Should_throw_ArgumentOutOfRangeException_When_the_patch_range_falls_outside_written_bytes")]
        public void Should_throw_ArgumentOutOfRangeException_When_the_patch_range_falls_outside_written_bytes()
        {
            using var writer = new PooledPayloadWriter();
            writer.GetSpan(4);
            writer.Advance(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetPatchSpan(0, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetPatchSpan(-1, 1));
        }

        // ===================== Detach =====================

        [Fact(DisplayName = "Should_hand_off_exactly_the_written_bytes_When_Detach_is_called")]
        public void Should_hand_off_exactly_the_written_bytes_When_Detach_is_called()
        {
            var writer = new PooledPayloadWriter(initialCapacityHint: 8);
            var data = MakeBytes(37, seed: 1);
            data.CopyTo(writer.GetSpan(data.Length));
            writer.Advance(data.Length);

            var owner = writer.Detach();

            Assert.Equal(data, owner.Memory.ToArray());

            // no throw, idempotent
            owner.Dispose();
            owner.Dispose();
        }

        [Fact(DisplayName = "Should_become_spent_When_Detach_is_called")]
        public void Should_become_spent_When_Detach_is_called()
        {
            var writer = new PooledPayloadWriter();
            writer.GetSpan(4);
            writer.Advance(4);

            using var owner = writer.Detach();

            Assert.Throws<ObjectDisposedException>(() => writer.WrittenCount);
            Assert.Throws<ObjectDisposedException>(() => { _ = writer.WrittenSpan; });
            Assert.Throws<ObjectDisposedException>(() => writer.WrittenMemory);
            Assert.Throws<ObjectDisposedException>(() => writer.GetSpan(1));
            Assert.Throws<ObjectDisposedException>(() => writer.GetMemory(1));
            Assert.Throws<ObjectDisposedException>(() => writer.Advance(1));
            Assert.Throws<ObjectDisposedException>(() => writer.GetPatchSpan(0, 1));
            Assert.Throws<ObjectDisposedException>(() => writer.Reset());
            Assert.Throws<ObjectDisposedException>(() => writer.Detach());

            // The writer's own Dispose is a no-op after Detach -- ownership already moved.
            writer.Dispose();
            writer.Dispose();
        }

        [Fact(DisplayName = "Should_throw_ObjectDisposedException_When_Detach_is_called_twice")]
        public void Should_throw_ObjectDisposedException_When_Detach_is_called_twice()
        {
            var writer = new PooledPayloadWriter();
            writer.GetSpan(1);
            writer.Advance(1);

            using var owner = writer.Detach();

            Assert.Throws<ObjectDisposedException>(() => writer.Detach());
        }

        // ===================== Dispose without Detach =====================

        [Fact(DisplayName = "Should_be_idempotent_When_Dispose_is_called_without_a_prior_Detach")]
        public void Should_be_idempotent_When_Dispose_is_called_without_a_prior_Detach()
        {
            var writer = new PooledPayloadWriter();
            writer.GetSpan(4);
            writer.Advance(4);

            writer.Dispose();
            writer.Dispose(); // no throw
        }

        // ===================== maxCapacity enforcement =====================

        [Fact(DisplayName = "Should_allow_writing_exactly_up_to_maxCapacity")]
        public void Should_allow_writing_exactly_up_to_maxCapacity()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8, maxCapacity: 16);
            var data = MakeBytes(16, seed: 2);

            data.CopyTo(writer.GetSpan(16));
            writer.Advance(16);

            Assert.Equal(16, writer.WrittenCount);
            Assert.Equal(data, writer.WrittenSpan.ToArray());
        }

        [Fact(DisplayName = "Should_throw_PayloadSizeExceededException_from_GetSpan_When_maxCapacity_would_be_exceeded")]
        public void Should_throw_PayloadSizeExceededException_from_GetSpan_When_maxCapacity_would_be_exceeded()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8, maxCapacity: 16);
            writer.GetSpan(16);
            writer.Advance(16); // fill exactly to the cap

            var ex = Assert.Throws<PayloadSizeExceededException>(() => writer.GetSpan(1));
            Assert.Equal(17, ex.AttemptedSize);
            Assert.Equal(16, ex.MaxCapacity);
        }

        [Fact(DisplayName = "Should_throw_PayloadSizeExceededException_from_GetMemory_When_maxCapacity_would_be_exceeded")]
        public void Should_throw_PayloadSizeExceededException_from_GetMemory_When_maxCapacity_would_be_exceeded()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8, maxCapacity: 16);
            writer.GetMemory(16);
            writer.Advance(16); // fill exactly to the cap

            var ex = Assert.Throws<PayloadSizeExceededException>(() => writer.GetMemory(1));
            Assert.Equal(17, ex.AttemptedSize);
            Assert.Equal(16, ex.MaxCapacity);
        }

        [Fact(DisplayName = "Should_throw_PayloadSizeExceededException_from_Advance_When_maxCapacity_would_be_exceeded")]
        public void Should_throw_PayloadSizeExceededException_from_Advance_When_maxCapacity_would_be_exceeded()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8, maxCapacity: 8);

            // Within the cap when requested, but a misbehaving caller advances further than it asked
            // for -- Advance must independently reject this, not merely trust a prior GetSpan check.
            writer.GetSpan(4);

            var ex = Assert.Throws<PayloadSizeExceededException>(() => writer.Advance(9));
            Assert.Equal(9, ex.AttemptedSize);
            Assert.Equal(8, ex.MaxCapacity);
        }

        [Fact(DisplayName = "Should_throw_ArgumentOutOfRangeException_When_maxCapacity_is_not_positive")]
        public void Should_throw_ArgumentOutOfRangeException_When_maxCapacity_is_not_positive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PooledPayloadWriter(maxCapacity: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PooledPayloadWriter(maxCapacity: -1));
        }

        // ===================== injectable buffer source =====================

        [Fact(DisplayName = "Should_route_every_rent_and_return_through_the_injected_pool_When_growing_then_detaching")]
        public void Should_route_every_rent_and_return_through_the_injected_pool_When_growing_then_detaching()
        {
            var pool = new TrackingArrayPool();
            var writer = new PooledPayloadWriter(initialCapacityHint: 8, pool: pool);

            // Write in chunks to force several growth re-rents past the tiny initial rent.
            var expected = new List<byte>();
            for (var i = 0; i < 10; i++)
            {
                var chunk = MakeBytes(100, seed: 100 + i);
                chunk.CopyTo(writer.GetSpan(chunk.Length));
                writer.Advance(chunk.Length);
                expected.AddRange(chunk);
            }

            Assert.True(pool.RentCount > 1, "expected at least one growth re-rent through the injected pool");
            Assert.Equal(pool.RentCount - 1, pool.ReturnCount); // every growth returned its old array; the live one is outstanding
            Assert.Equal(1, pool.Outstanding);

            using (var owner = writer.Detach())
            {
                Assert.Equal(expected.ToArray(), owner.Memory.ToArray());
            }

            // The detached owner's dispose returned the array to the SAME injected pool -- not Shared.
            Assert.Equal(pool.RentCount, pool.ReturnCount);
            Assert.Equal(0, pool.Outstanding);
            Assert.False(pool.SawForeignReturn, "an array was returned to the injected pool that it never rented out");
        }

        [Fact(DisplayName = "Should_return_the_buffer_to_the_injected_pool_When_disposed_without_a_prior_Detach")]
        public void Should_return_the_buffer_to_the_injected_pool_When_disposed_without_a_prior_Detach()
        {
            var pool = new TrackingArrayPool();
            var writer = new PooledPayloadWriter(initialCapacityHint: 8, pool: pool);

            for (var i = 0; i < 10; i++)
            {
                var chunk = MakeBytes(100, seed: 200 + i);
                chunk.CopyTo(writer.GetSpan(chunk.Length));
                writer.Advance(chunk.Length);
            }

            Assert.True(pool.RentCount > 1, "expected at least one growth re-rent through the injected pool");

            writer.Dispose();
            writer.Dispose(); // idempotent -- must not double-return to the pool

            Assert.Equal(pool.RentCount, pool.ReturnCount);
            Assert.Equal(0, pool.Outstanding);
            Assert.False(pool.SawForeignReturn, "an array was returned to the injected pool that it never rented out");
        }

        // ===================== Reset =====================

        [Fact(DisplayName = "Should_reuse_the_writer_for_new_content_When_Reset_is_called")]
        public void Should_reuse_the_writer_for_new_content_When_Reset_is_called()
        {
            using var writer = new PooledPayloadWriter(initialCapacityHint: 8);

            var first = MakeBytes(10, seed: 3);
            first.CopyTo(writer.GetSpan(first.Length));
            writer.Advance(first.Length);
            Assert.Equal(first.Length, writer.WrittenCount);

            writer.Reset();
            Assert.Equal(0, writer.WrittenCount);

            var second = MakeBytes(6, seed: 4);
            second.CopyTo(writer.GetSpan(second.Length));
            writer.Advance(second.Length);

            Assert.Equal(second.Length, writer.WrittenCount);
            Assert.Equal(second, writer.WrittenSpan.ToArray());
        }

        [Fact(DisplayName = "Should_throw_ObjectDisposedException_When_Reset_is_called_After_Detach")]
        public void Should_throw_ObjectDisposedException_When_Reset_is_called_After_Detach()
        {
            var writer = new PooledPayloadWriter();
            writer.GetSpan(1);
            writer.Advance(1);

            using var owner = writer.Detach();

            Assert.Throws<ObjectDisposedException>(() => writer.Reset());
        }

        // ===================== helpers =====================

        private static byte[] MakeBytes(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// An <see cref="ArrayPool{T}"/> that counts rents/returns and tracks array identity, so tests
        /// can prove the writer routes its ENTIRE buffer lifecycle -- initial rent, growth, dispose,
        /// and the detached owner's return -- through the injected pool rather than
        /// <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            private readonly ArrayPool<byte> _inner = Create();
            private readonly HashSet<byte[]> _outstanding = new();

            public int RentCount { get; private set; }
            public int ReturnCount { get; private set; }
            public int Outstanding => _outstanding.Count;
            public bool SawForeignReturn { get; private set; }

            public override byte[] Rent(int minimumLength)
            {
                var array = _inner.Rent(minimumLength);
                RentCount++;
                _outstanding.Add(array);
                return array;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!_outstanding.Remove(array))
                    SawForeignReturn = true;

                ReturnCount++;
                _inner.Return(array, clearArray);
            }
        }
    }
}
