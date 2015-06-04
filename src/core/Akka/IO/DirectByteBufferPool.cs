//-----------------------------------------------------------------------
// <copyright file="DirectByteBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Akka.IO
{
    public interface IBufferPool
    {
        ByteBuffer Acquire();
        void Release(ByteBuffer buf);
    }

    internal class DirectByteBufferPool : IBufferPool
    {
        private readonly ConcurrentStack<ByteBuffer> _pool;

        public DirectByteBufferPool()
        {
            var items = Enumerable.Range(0, PoolSize)
                                  .Select(x => new ByteBuffer(new byte[BufferSize]));
            _pool = new ConcurrentStack<ByteBuffer>(items);
        }

        public int BufferSize
        {
            get { return 128; }
        }

        public int PoolSize
        {
            get { return 128; }
        }

        public ByteBuffer Acquire()
        {
            ByteBuffer buffer;
            if (_pool.TryPop(out buffer))
            {
                buffer.Clear();
                return buffer;
            }
            throw new Exception(); // TODO: What do we do? throw proper exception?
        }

        public void Release(ByteBuffer buf)
        {
            _pool.Push(buf);
        }
    }
}
