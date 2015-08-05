//-----------------------------------------------------------------------
// <copyright file="TcpManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.IO
{
    internal class TcpManager : SelectionHandler.SelectorBasedManager
    {
        private readonly TcpExt _tcp;

        public TcpManager(TcpExt tcp)
            : base(tcp.Settings, tcp.Settings.NrOfSelectors)
        {
            _tcp = tcp;
        }

        protected override bool Receive(object m)
        {
            return WorkerForCommandHandler(message =>
            {
                var c = message as Tcp.Connect;
                if (c != null)
                {
                    var commander = Sender;
                    return registry => Props.Create(() => new TcpOutgoingConnection(_tcp, registry, commander, c));
                }
                var b = message as Tcp.Bind;
                if (b != null)
                {
                    var commander = Sender;
                    return registry => Props.Create(() => new TcpListener(SelectorPool, _tcp, registry, commander, b));
                }
                throw new ArgumentException("The supplied message type is invalid. Only Connect and Bind messages are supported.");
            })(m);
        }
    }
}