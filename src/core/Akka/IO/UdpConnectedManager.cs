//-----------------------------------------------------------------------
// <copyright file="UdpConnectedManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;

namespace Akka.IO
{
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
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
            switch (message)
            {
                case UdpConnected.Connect connect:
                    {
                        var commander = Sender; // NOTE: Aaronontheweb (9/1/2017) this should probably be the Handler...
                        Context.ActorOf(Props.Create(() => new UdpConnection(_udpConn, commander, connect)));
                        return true;
                    }
                case DeadLetter dl when dl.Message is UdpConnected.SocketCompleted completed:
                    {
                        var e = completed.EventArgs;
                        if (e.Buffer != null)
                        {
                            // no need to check for e.BufferList: release buffer only 
                            // on complete reads, which are always mono-buffered 
                            var buffer = new ByteBuffer(e.Buffer, e.Offset, e.Count);
                            _udpConn.BufferPool.Release(buffer);
                        }
                        _udpConn.SocketEventArgsPool.Release(e);
                        return true;
                    }
                case DeadLetter _: return true;
                default: throw new ArgumentException($"The supplied message type [{message.GetType()}] is invalid. Only Connect messages are supported.", nameof(message));

            }
        }

    }
}
