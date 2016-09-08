//-----------------------------------------------------------------------
// <copyright file="TcpOutgoingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.IO
{
    internal class TcpOutgoingConnection : TcpConnection
    {
        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;

        public TcpOutgoingConnection(TcpExt tcp, IActorRef commander, Tcp.Connect connect)
            : base(tcp, new Socket(SocketType.Stream, ProtocolType.Tcp) { Blocking = false }, connect.PullMode)
        {
            _commander = commander;
            _connect = connect;

            Context.Watch(commander);    // sign death pact

            connect.Options.ForEach(_ => _.BeforeConnect(Socket));
            if (connect.LocalAddress != null)
                Socket.Bind(connect.LocalAddress);
            if (connect.Timeout.HasValue)
                Context.SetReceiveTimeout(connect.Timeout.Value);  //Initiate connection timeout if supplied
        }

        private void Stop()
        {
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] {_commander}), _connect.FailureMessage));
        }

        private void ReportConnectFailure(Action thunk)
        {
            try
            {
                thunk();
            }
            catch (Exception e)
            {
                Log.Debug("Could not establish connection to [{0}] due to {1}", _connect.RemoteAddress, e);
                Stop();
            }
        }

        protected override void PreStart()
        {
            ReportConnectFailure(() =>
            {
                var remoteAddress = _connect.RemoteAddress as DnsEndPoint;
                if (remoteAddress != null)
                {
                    Log.Debug("Resolving {0} before connecting", remoteAddress.Host);
                    var resolved = Dns.ResolveName(remoteAddress.Host, Context.System, Self);
                    if (resolved == null)
                        Become(Resolving(remoteAddress));
                    else
                        Register(new IPEndPoint(resolved.Addr, remoteAddress.Port));
                }
                else
                {
                    Register(_connect.RemoteAddress);
                }
            });
        }

        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }

        private Receive Resolving(DnsEndPoint remoteAddress)
        {
            return message =>
            {
                var resolved = message as Dns.Resolved;
                if (resolved != null)
                {
                    ReportConnectFailure(() => Register(new IPEndPoint(resolved.Addr, remoteAddress.Port)));
                    return true;
                }
                return false;
            };
        }


        private void Register(EndPoint address)
        {
            ReportConnectFailure(() =>
            {
                Log.Debug("Attempting connection to [{0}]", address);

                var saea = Tcp.SocketEventArgsPool.Acquire(Self);
                saea.RemoteEndPoint = address;
                saea.SetBuffer(saea.Offset, 0);
                if (!Socket.ConnectAsync(saea))
                    Self.Tell(new SocketConnected(saea, Tcp.SocketEventArgsPool));
                Become(Connecting(Tcp.Settings.FinishConnectRetries));
            });
        }

        private Receive Connecting(int remainingFinishConnectRetries)
        {
            return message =>
            {
                if (message is SocketConnected)
                {
                    var connected = (SocketConnected)message;
                    if (connected.EventArgs.SocketError == SocketError.Success)
                    {
                        if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                        Log.Debug("Connection established to [{0}]", _connect.RemoteAddress);
                        CompleteConnect(_commander, _connect.Options);
                    }
                    else
                    {
                        Log.Debug("Could not establish connection because finishConnect never returned true (consider increasing akka.io.tcp.finish-connect-retries)");
                        Stop();
                    }
                    connected.Pool.Release(connected.EventArgs);
                    return true;
                }
                if (message is ReceiveTimeout)
                {
                    if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);  // Clear the timeout
                    Log.Debug("Connect timeout expired, could not establish connection to [{0}]", _connect.RemoteAddress);
                    Stop();
                    return true;
                }
                return false;
            };
        }
    }
}
