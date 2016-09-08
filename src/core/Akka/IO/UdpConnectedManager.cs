//-----------------------------------------------------------------------
// <copyright file="UdpConnectedManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.IO
{
    // INTERNAL API
    class UdpConnectedManager : ActorBase
    {
        private readonly UdpConnectedExt _udpConn;

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
                var completed = dl.Message as ISocketCompleted;
                if (completed != null && completed.Pool == _udpConn.SocketEventArgsPool)
                    completed.Pool.Release(completed.EventArgs);
                return true;
            }
            throw new ArgumentException("The supplied message type is invalid. Only Connect messages are supported.", nameof(message));
        }

    }
}
