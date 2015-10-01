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
    /**
     * TODO: CLRify comment
     * 
     * INTERNAL API
     *
     * TcpManager is a facade for accepting commands (<see cref="Akka.IO.Tcp.Command"/>) to open client or server TCP connections.
     *
     * TcpManager is obtainable by calling {{{ IO(Tcp) }}} (see [[akka.io.IO]] and [[akka.io.Tcp]])
     *
     * == Bind ==
     *
     * To bind and listen to a local address, a <see cref="Akka.IO.Tcp.Bind"/> command must be sent to this actor. If the binding
     * was successful, the sender of the <see cref="Akka.IO.Tcp.Bind"/> will be notified with a <see cref="Akka.IO.Tcp.Bound"/>
     * message. The sender() of the <see cref="Akka.IO.Tcp.Bound"/> message is the Listener actor (an internal actor responsible for
     * listening to server events). To unbind the port an <see cref="Akka.IO.Tcp.Unbind"/> message must be sent to the Listener actor.
     *
     * If the bind request is rejected because the Tcp system is not able to register more channels (see the nr-of-selectors
     * and max-channels configuration options in the akka.io.tcp section of the configuration) the sender will be notified
     * with a <see cref="Akka.IO.Tcp.CommandFailed"/> message. This message contains the original command for reference.
     *
     * When an inbound TCP connection is established, the handler will be notified by a <see cref="Akka.IO.Tcp.Connected"/> message.
     * The sender of this message is the Connection actor (an internal actor representing the TCP connection). At this point
     * the procedure is the same as for outbound connections (see section below).
     *
     * == Connect ==
     *
     * To initiate a connection to a remote server, a <see cref="Akka.IO.Tcp.Connect"/> message must be sent to this actor. If the
     * connection succeeds, the sender() will be notified with a <see cref="Akka.IO.Tcp.Connected"/> message. The sender of the
     * <see cref="Akka.IO.Tcp.Connected"/> message is the Connection actor (an internal actor representing the TCP connection). Before
     * starting to use the connection, a handler must be registered to the Connection actor by sending a <see cref="Akka.IO.Tcp.Register"/>
     * command message. After a handler has been registered, all incoming data will be sent to the handler in the form of
     * <see cref="Akka.IO.Tcp.Received"/> messages. To write data to the connection, a <see cref="Akka.IO.Tcp.Write"/> message must be sent
     * to the Connection actor.
     *
     * If the connect request is rejected because the Tcp system is not able to register more channels (see the nr-of-selectors
     * and max-channels configuration options in the akka.io.tcp section of the configuration) the sender will be notified
     * with a <see cref="Akka.IO.Tcp.CommandFailed"/> message. This message contains the original command for reference.
     *
     */
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