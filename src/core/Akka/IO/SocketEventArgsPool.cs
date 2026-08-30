//-----------------------------------------------------------------------
// <copyright file="SocketEventArgsPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Annotations;
using Akka.IO.Buffers;

namespace Akka.IO
{
    public interface ISocketEventArgsPool
    {
        SocketAsyncEventArgs Acquire(IActorRef actor);
        void Release(SocketAsyncEventArgs e);
        
        BufferPoolInfo BufferPoolInfo { get; }
    }

    // This class __does not__ pool and reuse SocketAsyncEventArgs anymore. Reusing SocketAsyncEventArgs with
    // multiple Socket instances is dangerous because SocketAsyncEventArgs is not a simple struct or POCO,
    // it actually held internal states that can wreak havoc if being used in another socket instance.
    // It is impossible to clear a SocketAsyncEventArgs object and the hassle of trying to handle every single
    // edge case outweigh the speed and memory gain of pooling the instances.
    internal class PreallocatedSocketEventAgrsPool : ISocketEventArgsPool
    {
        // Byte buffer pool is moved here to reduce the chance that a memory segment got mis-managed
        // and not released properly. We only need to worry about acquiring and releasing SocketAsyncEventArgs
        // and not worry about having to check to see if we need to rent or release any buffer.
        //
        // There is no reason why users or developers would need to touch memory management code because it is
        // very specific for providing byte buffers for SocketAsyncEventArgs
        private readonly IBufferPool _bufferPool;
        
        private readonly EventHandler<SocketAsyncEventArgs> _onComplete;

        public PreallocatedSocketEventAgrsPool(int initSize, IBufferPool bufferPool, EventHandler<SocketAsyncEventArgs> onComplete)
        {
            _bufferPool = bufferPool;
            _onComplete = onComplete;
        }

        
        public SocketAsyncEventArgs Acquire(IActorRef actor)
        {
            var buffer = _bufferPool.Rent();
            var e = new SocketAsyncEventArgs();

            e.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
            e.UserToken = actor;
            e.Completed += _onComplete;
            return e;
        }

        public void Release(SocketAsyncEventArgs e)
        {
            if (e.Buffer != null)
            {
                _bufferPool.Release(new ByteBuffer(e.Buffer, e.Offset, e.Count));
            }
            if (e.BufferList != null)
            {
                foreach (var segment in e.BufferList)
                {
                    _bufferPool.Release(segment);
                }
            }
            e.Dispose();
        }

        public BufferPoolInfo BufferPoolInfo => _bufferPool.Diagnostics();
    }

}
