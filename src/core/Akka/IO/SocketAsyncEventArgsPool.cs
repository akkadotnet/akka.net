//-----------------------------------------------------------------------
// <copyright file="SocketAsyncEventArgsPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;

namespace Akka.IO
{
    public class SocketAsyncEventArgsPool
    {
        private readonly ConcurrentStack<SocketAsyncEventArgs> _pool;
        private readonly byte[] _buffer;

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

        public int BufferSize
        {
            get { return 128; } // TODO: make configurable with good default
        }

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

        public void Return(SocketAsyncEventArgs saea)
        {
            saea.UserToken = null;
            _pool.Push(saea);
        }
    }
}