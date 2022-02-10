//-----------------------------------------------------------------------
// <copyright file="SocketEventArgsPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Util;

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
                _bufferPool.Release(new ArraySegment<byte>(e.Buffer, e.Offset, e.Count));
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

    internal static class SocketAsyncEventArgsExtensions
    {
        public static void SetBuffer(this SocketAsyncEventArgs args, ByteString data)
        {
            if (data.IsCompact)
            {
                var buffer = data.Buffers[0];
                if (args.BufferList != null)
                {
                    // BufferList property setter is not simple member association operation, 
                    // but the getter is. Therefore we first check if we need to clear buffer list
                    // and only do so if necessary.
                    args.BufferList = null;
                }
                args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
            }
            else
            {
                if (RuntimeDetector.IsMono)
                {
                    // Mono doesn't support BufferList - falback to compacting ByteString
                    var compacted = data.Compact();
                    var buffer = compacted.Buffers[0];
                    args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
                }
                else
                {
                    args.SetBuffer(null, 0, 0);
                    args.BufferList = data.Buffers;
                }
            }
        }
        
        public static void SetBuffer(this SocketAsyncEventArgs args, IEnumerable<ByteString> dataCollection)
        {
            if (RuntimeDetector.IsMono)
            {
                // Mono doesn't support BufferList - falback to compacting ByteString
                var dataList = dataCollection.ToList();
                var totalSize = dataList.SelectMany(d => d.Buffers).Sum(d => d.Count);
                var bytes = new byte[totalSize];
                var position = 0;
                foreach (var byteString in dataList)
                {
                    var copied = byteString.CopyTo(bytes, position, byteString.Count);
                    position += copied;
                }
                args.SetBuffer(bytes, 0, bytes.Length);
            }
            else
            {
                args.SetBuffer(null, 0, 0);
                args.BufferList = dataCollection.SelectMany(d => d.Buffers).ToList();
            }
        }
    }
}
