﻿//-----------------------------------------------------------------------
// <copyright file="UdpConnectedManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.IO
{
    // INTERNAL API
    class UdpConnectedManager : SelectionHandler.SelectorBasedManager
    {
        private readonly UdpConnectedExt _udpConn;

        public UdpConnectedManager(UdpConnectedExt udpConn)
            : base(udpConn.Settings, udpConn.Settings.NrOfSelectors)
        {
            _udpConn = udpConn;
        }

        protected override bool Receive(object m)
        {
            return WorkerForCommandHandler(message =>
            {
                var c = message as UdpConnected.Connect;
                if (c != null)
                {
                    var commander = Sender;
                    return registry => Props.Create(() => new UdpConnection(_udpConn, registry, commander, c));
                }
                throw new ArgumentException("The supplied message type is invalid. Only Connect messages are supported.");
            })(m);
        }

    }
}
