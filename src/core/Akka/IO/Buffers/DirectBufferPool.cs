//-----------------------------------------------------------------------
// <copyright file="DirectBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO.Buffers
{
    using ByteBuffer = ArraySegment<byte>;

    public class BufferPoolAllocationException : AkkaException
    {
        public BufferPoolAllocationException(string message) : base(message)
        {
        }
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
        /// buffers are expected to be released using <see cref="Release(System.ArraySegment{byte})"/> 
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
    }

    /// <summary>
    /// Direct buffer pool is used by <see cref="TcpExt"/>, <see cref="UdpExt"/>, <see cref="UdpConnectedExt"/>
    /// for receving reusable buffers for receving data without triggering too much GC pauses.
    /// 
    /// For performance bytes are allocated in large blocks called segments. Then each segment is cut into
    /// pieces (byte buffers). Those buffers are rent to the callers, and need to be released later. If buffer
    /// pool gets saturated, a new segments it created (and therefore a new collection of byte buffers to be 
    /// used). Once allocated segments are never released until their owner (ActorSystem extension) is not 
    /// disposed - which usually happens, when actor system is terminated.
    /// 
    /// In order to avoid infinite growing of memory, a maximum number of segments can be configured (see:
    /// `akka.io.[extension-type].direct-buffer-pool.buffer-pool-limit`. Once it will be fully saturated,
    /// an <see cref="BufferPoolAllocationException"/> will be thrown.
    /// </summary>
    internal sealed class DirectBufferPool : IBufferPool
    {
        private const int Retries = 30;
        private readonly object _syncRoot = new object();

        private readonly int _bufferSize;
        private readonly int _buffersPerSegment;
        private readonly int _segmentSize;
        private readonly int _maxSegmentCount;

        private readonly ConcurrentStack<ByteBuffer> _buffers = new ConcurrentStack<ByteBuffer>();
        private readonly List<byte[]> _segments;

        /// <summary>
        /// Total size of all buffers.
        /// </summary>
        private int TotalBufferSize => _segments.Count * _segmentSize;

        public DirectBufferPool(ExtendedActorSystem system, Config config) : this(
                  bufferSize: config.GetInt("buffer-size", 256),
                  buffersPerSegment: config.GetInt("buffers-per-segment", 250),
                  initialSegments: config.GetInt("initial-segments", 1),
                  maxSegments: config.GetInt("buffer-pool-limit", 1000))
        {
        }

        public DirectBufferPool(int bufferSize, int buffersPerSegment, int initialSegments, int maxSegments)
        {
            if (bufferSize <= 0) throw new ArgumentException("Buffer size must be positive number", nameof(bufferSize));
            if (buffersPerSegment <= 0) throw new ArgumentException("Number of buffers per segment must be positive", nameof(buffersPerSegment));
            if (initialSegments <= 0) throw new ArgumentException("Number of initial segments must be positivie", nameof(initialSegments));
            if (maxSegments < initialSegments) throw new ArgumentException("Maximum number of segments must not be less than the initial one", nameof(maxSegments));

            _bufferSize = bufferSize;
            _buffersPerSegment = buffersPerSegment;
            _segmentSize = bufferSize * buffersPerSegment;
            _maxSegmentCount = maxSegments;

            _segments = new List<byte[]>(initialSegments);
            for (int i = 0; i < initialSegments; i++)
            {
                AllocateSegment();
            }
        }

        private void AllocateSegment()
        {
            lock (_syncRoot)
            {
                if (_segments.Count >= _maxSegmentCount)
                    throw new BufferPoolAllocationException($"Buffer pool cannot allocate more segments. A maximum capacity of {_maxSegmentCount * _segmentSize} bytes has been reached.");

                var segment = new byte[_segmentSize];
                _segments.Add(segment);
                for (int i = 0; i < _buffersPerSegment; i++)
                {
                    var buf = new ByteBuffer(segment, i * _bufferSize, _bufferSize);
                    _buffers.Push(buf);
                }
            }
        }

        public ByteBuffer Rent()
        {
            for (int i = 0; i < Retries; i++)
            {
                ByteBuffer buf;
                if (_buffers.TryPop(out buf))
                    return buf;
                AllocateSegment();
            }

            throw new BufferPoolAllocationException($"Buffer pool failed to return byte buffer after {Retries} retries made.");
        }

        public IEnumerable<ByteBuffer> Rent(int minimumSize)
        {
            var buffersToGet = (int)Math.Ceiling((double) minimumSize / _bufferSize);
            var result = new ByteBuffer[buffersToGet];
            var received = 0;
            try
            {
                for (int i = 0; i < Retries; i++)
                {
                    ByteBuffer buf;
                    while (received < buffersToGet)
                    {
                        if (!_buffers.TryPop(out buf)) break;
                        result[received] = buf;
                        received++;
                    }

                    if (received == buffersToGet) return result;

                    AllocateSegment();
                }

                throw new BufferPoolAllocationException($"Couldn't allocate enough byte buffer to fill the tolal requested size of {minimumSize} bytes");
            }
            catch 
            {
                for (int i = 0; i < received; i++) Release(result[i]);
                throw;
            }
        }

        public void Release(ByteBuffer buf)
        {
            // only release buffers that have actually been taken from one of the segments
            if (buf.Count == _bufferSize && _segments.Contains(buf.Array))
                _buffers.Push(buf);
        }

        public void Release(IEnumerable<ByteBuffer> buffers)
        {
            foreach (var buf in buffers)
            {
                Release(buf);
            }
        }
    }
}
