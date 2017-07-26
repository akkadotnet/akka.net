﻿//-----------------------------------------------------------------------
// <copyright file="DirectByteBufferPool.cs" company="Akka.NET Project">
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
                // it can be that for some reason socket is in use and haven't closed yet
            }
        }

        private SocketAsyncEventArgs CreateSocketAsyncEventArgs()
        {
            var e = new SocketAsyncEventArgs { UserToken = null };
            e.Completed += _onComplete;
            return e;
        }
    }
}
