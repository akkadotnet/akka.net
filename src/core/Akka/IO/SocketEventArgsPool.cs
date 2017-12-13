//-----------------------------------------------------------------------
// <copyright file="SocketEventArgsPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.IO.Buffers;
using Akka.Util;

namespace Akka.IO
{
    public interface ISocketEventArgsPool
    {
        SocketAsyncEventArgs Acquire(IActorRef actor);
        void Release(SocketAsyncEventArgs e);
    }

    internal class PreallocatedSocketEventAgrsPool : ISocketEventArgsPool
    {
        private readonly EventHandler<SocketAsyncEventArgs> _onComplete;
        private readonly ConcurrentStack<SocketAsyncEventArgs> _pool = new ConcurrentStack<SocketAsyncEventArgs>();

        private int active = 0;

        public PreallocatedSocketEventAgrsPool(int initSize, EventHandler<SocketAsyncEventArgs> onComplete)
        {
            _onComplete = onComplete;
            for (int i = 0; i < initSize; i++, active++)
            {
                var e = CreateSocketAsyncEventArgs();
                _pool.Push(e);
            }
        }

        public SocketAsyncEventArgs Acquire(IActorRef actor)
        {
            SocketAsyncEventArgs e;
            if (!_pool.TryPop(out e))
            {
                e = CreateSocketAsyncEventArgs();
                active++;
            }
            e.UserToken = actor;
            return e;
        }

        public void Release(SocketAsyncEventArgs e)
        {
            e.UserToken = null;
            e.AcceptSocket = null;

            try
            {
                e.SetBuffer(null, 0, 0);
                if (_pool.Count < 2048) // arbitrary taken max amount of free SAEA stored
                {
                    _pool.Push(e);
                }
                else
                {
                    e.Dispose();
                    active--;
                }
            }
            catch (InvalidOperationException)
            {
                // it can be that for some reason socket is in use and haven't closed yet. Dispose anyway to avoid leaks.
                e.Dispose();
                active--;
            }
        }

        private SocketAsyncEventArgs CreateSocketAsyncEventArgs()
        {
            var e = new SocketAsyncEventArgs { UserToken = null };
            e.Completed += _onComplete;
            return e;
        }
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
    }
}
