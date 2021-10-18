// //-----------------------------------------------------------------------
// // <copyright file="SimpleBufferPool.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO.Buffers
{
    using ByteBuffer = ArraySegment<byte>;
    
    public class SimpleBufferPool : IBufferPool
    {
        private readonly ConcurrentStack<ByteBuffer> _buffers = new ConcurrentStack<ByteBuffer>();
        private readonly int _bufferSize;
        private readonly int _capacity;

        public SimpleBufferPool(ExtendedActorSystem system, Config config) : this(
            bufferSize: config.GetInt("buffer-size", 256),
            capacity: config.GetInt("buffer-pool-capacity", 250000))
        {
        }

        public SimpleBufferPool(int bufferSize, int capacity)
        {
            if (bufferSize <= 0) throw new ArgumentException("Buffer size must be positive number", nameof(bufferSize));
            if (capacity <= 0) throw new ArgumentException("Number of maximum pool capacity must be positive", nameof(capacity));

            _bufferSize = bufferSize;
            _capacity = capacity;
        }
        
        public ByteBuffer Rent()
        {
            return _buffers.TryPop(out var buffer) 
                ? buffer 
                : new ByteBuffer(new byte[_bufferSize], 0, _bufferSize);
        }

        public IEnumerable<ByteBuffer> Rent(int minimumSize)
        {
            var buffersToGet = (int)Math.Ceiling((double) minimumSize / _bufferSize);
            var result = new ByteBuffer[buffersToGet];
            var received = 0;
            
            while (received < buffersToGet)
            {
                result[received] = Rent();
                received++;
            }

            return result;
        }

        public void Release(ByteBuffer buf)
        {
            if(_buffers.Count < _capacity)
                _buffers.Push(buf);
            // We don't care about pool overrun, we just let the runtime GC them
        }

        public void Release(IEnumerable<ByteBuffer> bufs)
        {
            foreach (var buf in bufs)
            {
                if(_buffers.Count < _capacity)
                    _buffers.Push(buf);
                else
                    return; // We don't care about the rest, let the runtime GC them
            }
        }
    }
}