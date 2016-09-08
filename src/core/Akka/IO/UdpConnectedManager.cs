//-----------------------------------------------------------------------
// <copyright file="UdpConnectedManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.IO
{
    using ByteBuffer = ArraySegment<byte>;

    // INTERNAL API
    class UdpConnectedManager : ActorBase
    {
        private readonly UdpConnectedExt _udpConn;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="udpConn">TBD</param>
        public UdpConnectedManager(UdpConnectedExt udpConn)
        {
            _udpConn = udpConn;
            Context.System.EventStream.Subscribe(Self, typeof(DeadLetter));
        }
        
        protected override bool Receive(object message)
        {
            var c = message as UdpConnected.Connect;
            if (c != null)
            {
                var commander = Sender;
                Context.ActorOf(Props.Create(() => new UdpConnection(_udpConn, commander, c)));
                return true;
            }
            var dl = message as DeadLetter;
            if (dl != null)
            {
                var completed = dl.Message as UdpConnected.SocketCompleted;
                if (completed != null)
                {
                    var e = completed.EventArgs;
                    if (e.Buffer != null)
                    {
                        var buffer = new ByteBuffer(e.Buffer, e.Offset, e.Count);
                        _udpConn.BufferPool.Release(buffer);
                    }
                    _udpConn.SocketEventArgsPool.Release(e);
                }
                return true;
            }
            throw new ArgumentException("The supplied message type is invalid. Only Connect messages are supported.", nameof(message));
        }

    }
}
#endif