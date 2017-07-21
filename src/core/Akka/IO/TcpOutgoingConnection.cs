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
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class TcpOutgoingConnection : TcpConnection
    {
        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;
        
        public TcpOutgoingConnection(TcpExt tcp, IActorRef commander, Tcp.Connect connect)
            : base(tcp, new Socket(connect.RemoteAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false }, connect.PullMode)
        {
            _commander = commander;
            _connect = connect;

            SignDeathPact(commander);

            foreach (var option in connect.Options)
            {
                option.BeforeConnect(Socket);
            }
            
            if (connect.LocalAddress != null)
                Socket.Bind(connect.LocalAddress);

            if (connect.Timeout.HasValue)
                Context.SetReceiveTimeout(connect.Timeout.Value);  //Initiate connection timeout if supplied
        }

        private void Stop()
        {
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] {_commander}), _connect.FailureMessage));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                if (_connect.RemoteAddress is DnsEndPoint)
                {
                    var remoteAddress = (DnsEndPoint) _connect.RemoteAddress;
                    Log.Debug("Resolving {0} before connecting", remoteAddress.Host);
                    var resolved = Dns.ResolveName(remoteAddress.Host, Context.System, Self);
                    if (resolved == null)
                        Become(Resolving(remoteAddress));
                    else
                        Register(new IPEndPoint(resolved.Addr, remoteAddress.Port));
                }
                else if(_connect.RemoteAddress is IPEndPoint)
                {
                    Register((IPEndPoint)_connect.RemoteAddress);
                }
                else throw new NotSupportedException($"Couldn't connect to [{_connect.RemoteAddress}]: only IP and DNS-based endpoints are supported");
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


        private void Register(IPEndPoint address)
        {
            ReportConnectFailure(() =>
            {
                Log.Debug("Attempting connection to [{0}]", address);

                var connectArgs = Tcp.SocketEventArgsPool.Acquire(Self);
                connectArgs.RemoteEndPoint = address;
                // we don't setup buffer here, it shouldn't be necessary just for connection
                if (!Socket.ConnectAsync(connectArgs))
                    Self.Tell(IO.Tcp.SocketConnected.Instance);

                Become(Connecting(Tcp.Settings.FinishConnectRetries, connectArgs));
            });
        }

        private Receive Connecting(int remainingFinishConnectRetries, SocketAsyncEventArgs args)
        {
            return message =>
            {
                if (message is IO.Tcp.SocketConnected)
                {
                    if (args.SocketError == SocketError.Success)
                    {
                        if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                        Log.Debug("Connection established to [{0}]", _connect.RemoteAddress);

                        AcquireSocketAsyncEventArgs();

                        CompleteConnect(_commander, _connect.Options);
                    }
                    else if (remainingFinishConnectRetries > 0)
                    {
                        var self = Self;
                        Context.System.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(1), () =>
                        {
                            if (!Socket.ConnectAsync(args))
                                self.Tell(IO.Tcp.SocketConnected.Instance);
                        });
                        Context.Become(Connecting(remainingFinishConnectRetries - 1, args));
                    }
                    else
                    {
                        Log.Debug("Could not establish connection because finishConnect never returned true (consider increasing akka.io.tcp.finish-connect-retries)");
                        Stop();
                    }
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