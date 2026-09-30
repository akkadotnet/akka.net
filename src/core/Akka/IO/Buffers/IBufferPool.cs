//-----------------------------------------------------------------------
// <copyright file="IBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.IO.Buffers
{
    public class BufferPoolAllocationException : AkkaException
    {
        public BufferPoolAllocationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferPoolAllocationException" /> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected BufferPoolAllocationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    public class BufferPoolInfo
    {
        public BufferPoolInfo(Type type, long totalSize, long free, long used)
        {
            Type = type;
            TotalSize = totalSize;
            Free = free;
            Used = used;
        }

        public Type Type { get; }
        public long TotalSize { get; }
        public long Free { get; }
        public long Used { get; }
    }
    
    /// <summary>
    /// An interface used to acquire/release recyclable chunks of 
    /// bytes to be reused without need to triggering GC.
    /// </summary>
    public interface IBufferPool
    {
        /// <summary>
        /// Rents a byte buffer representing a single continuous block of memory. Size of byte buffer
        /// is dependent from the implementation. Once rent, byte buffers are expected to be released
        /// using <see cref="Release(System.ArraySegment{byte})"/> method.
        /// </summary>
        /// <returns></returns>
        ByteBuffer Rent();

        /// <summary>
        /// Rents a sequence of byte buffers representing (potentially non-continuous) range of memory
        /// that is big enough to fit the <paramref name="minimumSize"/> requested. Once rent, byte
        /// buffers are expected to be released using <see cref="Release(ByteBuffer)"/>
        /// method.
        /// </summary>
        /// <param name="minimumSize">
        /// Minimum size in bytes, that returned collection of byte buffers must be able to fit.
        /// </param>
        /// <returns></returns>
        IEnumerable<ByteBuffer> Rent(int minimumSize);

        /// <summary>
        /// Releases a single byte buffer for further use.
        /// </summary>
        /// <param name="buf"></param>
        void Release(ByteBuffer buf);

        /// <summary>
        /// Releases a collection of previously allocated byte buffers for further use.
        /// </summary>
        /// <param name="buf"></param>
        void Release(IEnumerable<ByteBuffer> buf);

        BufferPoolInfo Diagnostics();
    }
}
