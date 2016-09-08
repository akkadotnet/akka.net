//-----------------------------------------------------------------------
// <copyright file="DirectByteBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace Akka.IO
{
    public interface ISocketEventArgsPool
    {
        SocketAsyncEventArgs Acquire(IActorRef actor);
        void Release(SocketAsyncEventArgs buf);
    }

    interface ISocketCompleted
    {
        SocketAsyncEventArgs EventArgs { get; }
        ISocketEventArgsPool Pool { get; }
    }

    struct SocketReceived : ISocketCompleted
    {
        public SocketAsyncEventArgs EventArgs { get; }
        public ISocketEventArgsPool Pool { get; }

        public SocketReceived(SocketAsyncEventArgs eventArgs, ISocketEventArgsPool pool)
        {
            EventArgs = eventArgs;
            Pool = pool;
        }
    }
    struct SocketSent : ISocketCompleted
    {
        public SocketAsyncEventArgs EventArgs { get; }
        public ISocketEventArgsPool Pool { get; }

        public SocketSent(SocketAsyncEventArgs eventArgs, ISocketEventArgsPool pool)
        {
            EventArgs = eventArgs;
            Pool = pool;
        }
    }
    struct SocketConnected : ISocketCompleted
    {
        public SocketAsyncEventArgs EventArgs { get; }
        public ISocketEventArgsPool Pool { get; }

        public SocketConnected(SocketAsyncEventArgs eventArgs, ISocketEventArgsPool pool)
        {
            EventArgs = eventArgs;
            Pool = pool;
        }
    }
    struct SocketAccepted : ISocketCompleted
    {
        public SocketAsyncEventArgs EventArgs { get; }
        public ISocketEventArgsPool Pool { get; }

        public SocketAccepted(SocketAsyncEventArgs eventArgs, ISocketEventArgsPool pool)
        {
            EventArgs = eventArgs;
            Pool = pool;
        }
    }
    static class SocketCompletedExtensions
    {
        public static void Release(this ISocketCompleted e)
        {
            e.Pool.Release(e.EventArgs);
        }
    }

    internal class PreallocatedSocketEventAgrsPool : ISocketEventArgsPool
    {
        private readonly int _defaultBufferSize;
        //private readonly int _maxPoolEntries;
        private readonly ConcurrentStack<SocketAsyncEventArgs> _pool;
        private readonly Dictionary<SocketAsyncOperation, Func<SocketAsyncEventArgs, ISocketCompleted>> _factory;
        //private int _buffersInPool;
        byte[] _buffer;

        public PreallocatedSocketEventAgrsPool(int defaultBufferSize, int maxPoolEntries)
        {
            _defaultBufferSize = defaultBufferSize;
            //_maxPoolEntries = maxPoolEntries;
            _buffer = new byte[defaultBufferSize * maxPoolEntries]; 
            _pool = new ConcurrentStack<SocketAsyncEventArgs>(Enumerable.Range(0, maxPoolEntries).Select(Allocate));

            _factory = new Dictionary<SocketAsyncOperation, Func<SocketAsyncEventArgs, ISocketCompleted>>
            {
                [SocketAsyncOperation.Accept] = e => new SocketAccepted(e, this),
                [SocketAsyncOperation.Connect] = e => new SocketConnected(e, this),
                [SocketAsyncOperation.Receive] = e => new SocketReceived(e, this),
                [SocketAsyncOperation.ReceiveFrom] = e => new SocketReceived(e, this),
                [SocketAsyncOperation.Send] = e => new SocketSent(e, this),
                [SocketAsyncOperation.SendTo] = e => new SocketSent(e, this)
            };
        }

        private void OnCompleted(object sender, SocketAsyncEventArgs e)
        {
            (e.UserToken as IActorRef).Tell(_factory[e.LastOperation](e));
        }

        public SocketAsyncEventArgs Acquire(IActorRef actor)
        {
            var sea = TakeBufferFromPool();
            sea.UserToken = actor;
            return sea;
        }

        public void Release(SocketAsyncEventArgs buf)
        {
            OfferBufferToPool(buf);
        }

        private SocketAsyncEventArgs Allocate(int i)
        {
            var saea = new SocketAsyncEventArgs();
            saea.SetBuffer(_buffer, i * _defaultBufferSize, _defaultBufferSize);
            saea.Completed += OnCompleted;
            return saea;
        }

        private SocketAsyncEventArgs TakeBufferFromPool()
        {
            SocketAsyncEventArgs ea;
            while (!_pool.TryPop(out ea)) ;
            return ea;
        }

        private void OfferBufferToPool(SocketAsyncEventArgs saea)
        {
            saea.SetBuffer(saea.Offset, _defaultBufferSize);
            saea.UserToken = null;
            _pool.Push(saea);
        }
    }
}
