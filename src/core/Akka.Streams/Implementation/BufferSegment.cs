//-----------------------------------------------------------------------
// <copyright file="BufferSegment.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;

#nullable enable

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Linked-list segment used to compose a multi-segment <see cref="ReadOnlySequence{T}"/>
    /// without copying the underlying memory. Used by framing stages and the JSON parser
    /// when multiple input chunks need to be appended to an internal buffer.
    /// </summary>
    internal sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }

        /// <summary>
        /// Builds a non-allocating concatenation of <paramref name="first"/> and
        /// <paramref name="second"/> by chaining their underlying segments.
        /// Allocates only the segment nodes — no <c>byte[]</c> data copy.
        /// </summary>
        public static ReadOnlySequence<byte> Concat(ReadOnlySequence<byte> first, ReadOnlySequence<byte> second)
        {
            if (first.IsEmpty) return second;
            if (second.IsEmpty) return first;

            BufferSegment? head = null;
            BufferSegment? tail = null;

            foreach (var mem in first)
            {
                if (head is null)
                {
                    head = new BufferSegment(mem);
                    tail = head;
                }
                else
                {
                    tail = tail!.Append(mem);
                }
            }

            foreach (var mem in second)
            {
                tail = tail!.Append(mem);
            }

            return new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
        }
    }
}
