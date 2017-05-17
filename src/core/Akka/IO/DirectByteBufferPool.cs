//-----------------------------------------------------------------------
// <copyright file="DirectByteBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IBufferPool
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        ByteBuffer Acquire();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buf">TBD</param>
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
        private readonly object poolLock = new object();
        private int _buffersInPool;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultBufferSize">TBD</param>
        /// <param name="maxPoolEntries">TBD</param>
        public DirectByteBufferPool(int defaultBufferSize, int maxPoolEntries)
        {
            _defaultBufferSize = defaultBufferSize;
            _maxPoolEntries = maxPoolEntries;
            _pool = new ByteBuffer[maxPoolEntries];
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public ByteBuffer Acquire()
        {
            return TakeBufferFromPool();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buf">TBD</param>
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
            lock (poolLock)
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
            lock (poolLock)
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
#endif