//-----------------------------------------------------------------------
// <copyright file="UdpConnectedManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using Akka.Actor;

namespace Akka.IO
{
    // INTERNAL API	
    /// <summary>
    /// TBD
    /// </summary>
    class UdpConnectedManager : SelectionHandler.SelectorBasedManager
    {
        private readonly UdpConnectedExt _udpConn;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="udpConn">TBD</param>
        public UdpConnectedManager(UdpConnectedExt udpConn)
            : base(udpConn.Settings, udpConn.Settings.NrOfSelectors)
        {
            _udpConn = udpConn;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="m">TBD</param>
        /// <returns>TBD</returns>
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
                throw new ArgumentException("The supplied message type is invalid. Only Connect messages are supported.", nameof(m));
            })(m);
        }

    }
}
#endif