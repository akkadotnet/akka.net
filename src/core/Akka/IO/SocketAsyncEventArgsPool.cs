//-----------------------------------------------------------------------
// <copyright file="SocketAsyncEventArgsPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SocketAsyncEventArgsPool
    {
        private readonly ConcurrentStack<SocketAsyncEventArgs> _pool;
        private readonly byte[] _buffer;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        /// <param name="select">TBD</param>
        public SocketAsyncEventArgsPool(int capacity, Action<SocketAsyncEventArgs> select)
        {
            _buffer = new byte[capacity*BufferSize];
            var items = Enumerable.Range(0, capacity).Select(i =>
            {
                var saea = new SocketAsyncEventArgs();
                saea.Completed += (s, e) => select(e);
                saea.SetBuffer(_buffer, i*BufferSize, BufferSize);
                return saea;
            });
            _pool = new ConcurrentStack<SocketAsyncEventArgs>(items);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int BufferSize
        {
            get { return 128; } // TODO: make configurable with good default
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="token">TBD</param>
        /// <returns>TBD</returns>
        public SocketAsyncEventArgs Request(object token)
        {
            SocketAsyncEventArgs saea;
            if (_pool.TryPop(out saea))
            {
                saea.UserToken = token;
                saea.SetBuffer(saea.Offset, BufferSize);
                return saea;
            }
            throw new Exception(); //TODO: What do we do when pool is empty?
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="saea">TBD</param>
        public void Return(SocketAsyncEventArgs saea)
        {
            saea.UserToken = null;
            _pool.Push(saea);
        }
    }
}
#endif