//-----------------------------------------------------------------------
// <copyright file="DirectByteBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.IO
{
    public interface IBufferPool
    {
        ByteBuffer Acquire();
        void Release(ByteBuffer buf);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// A buffer pool which keeps a free list of direct buffers of a specified default
    /// size in a simple fixed size stack.
    ///
    /// If the stack is full the buffer is de-referenced and available to be
    /// freed by normal garbage collection.
    /// </summary>
    internal class DirectByteBufferPool : IBufferPool
    {
        private readonly int _defaultBufferSize;
        private readonly int _maxPoolEntries;
        private readonly ByteBuffer[] _pool;
        private int _buffersInPool;

        public DirectByteBufferPool(int defaultBufferSize, int maxPoolEntries)
        {
            _defaultBufferSize = defaultBufferSize;
            _maxPoolEntries = maxPoolEntries;
            _pool = new ByteBuffer[maxPoolEntries];
        }


        public ByteBuffer Acquire()
        {
            return TakeBufferFromPool();
        }

        public void Release(ByteBuffer buf)
        {
            OfferBufferToPool(buf);
        }

        private ByteBuffer Allocate(int size)
        {
            return ByteBuffer.Allocate(size);
        }

        private ByteBuffer TakeBufferFromPool()
        {
            ByteBuffer buffer = null;
            lock (_pool.SyncRoot)
            {
                if (_buffersInPool > 0)
                {
                    _buffersInPool -= 1;
                    buffer = _pool[_buffersInPool];
                }
            }
            if (buffer == null)
                return Allocate(_defaultBufferSize);
            buffer.Clear();
            return buffer;
        }

        private void OfferBufferToPool(ByteBuffer buf)
        {
            lock (_pool.SyncRoot)
            {
                if (_buffersInPool < _maxPoolEntries)
                {
                    _pool[_buffersInPool] = buf;
                    _buffersInPool += 1;
                }  // else let the buffer be gc'd
            }
        }
    }
}
