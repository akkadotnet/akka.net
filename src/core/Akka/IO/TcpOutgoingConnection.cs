//-----------------------------------------------------------------------
// <copyright file="TcpOutgoingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Util;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class TcpOutgoingConnection : TcpConnection
    {
        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;

        private SocketAsyncEventArgs _connectArgs;

        public TcpOutgoingConnection(TcpExt tcp, IActorRef commander, Tcp.Connect connect)
            : base(
                   tcp,
                   tcp.Settings.OutgoingSocketForceIpv4
                       ? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false }
                       : new Socket(SocketType.Stream, ProtocolType.Tcp) { Blocking = false },
                   connect.PullMode,
                   tcp.Settings.WriteCommandsQueueMaxSize >= 0 ? tcp.Settings.WriteCommandsQueueMaxSize : Option<int>.None)
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

        private void ReleaseConnectionSocketArgs()
        {
            if (_connectArgs != null)
            {
                ReleaseSocketEventArgs(_connectArgs);
                _connectArgs = null;
            }
        }

        private void Stop()
        {
            ReleaseConnectionSocketArgs();

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
                Log.Error(e, "Could not establish connection to [{0}].", _connect.RemoteAddress);
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
                    else if(resolved.Ipv4.Any() && resolved.Ipv6.Any()) // one of both families
                        Register(new IPEndPoint(resolved.Ipv4.FirstOrDefault(), remoteAddress.Port), new IPEndPoint(resolved.Ipv6.FirstOrDefault(), remoteAddress.Port));
                    else // one or the other
                        Register(new IPEndPoint(resolved.Addr, remoteAddress.Port), null);
                }
                else if(_connect.RemoteAddress is IPEndPoint)
                {
                    Register((IPEndPoint)_connect.RemoteAddress, null);
                }
                else throw new NotSupportedException($"Couldn't connect to [{_connect.RemoteAddress}]: only IP and DNS-based endpoints are supported");
            });
        }

        protected override void PostStop()
        {
            // always try to release SocketAsyncEventArgs to avoid memory leaks
            ReleaseConnectionSocketArgs();

            base.PostStop();
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
                    if (resolved.Ipv4.Any() && resolved.Ipv6.Any()) // multiple addresses
                    {
                        ReportConnectFailure(() => Register(
                            new IPEndPoint(resolved.Ipv4.FirstOrDefault(), remoteAddress.Port),
                            new IPEndPoint(resolved.Ipv6.FirstOrDefault(), remoteAddress.Port)));
                    }
                    else // only one address family. No fallbacks.
                    {
                        ReportConnectFailure(() => Register(
                            new IPEndPoint(resolved.Addr, remoteAddress.Port),
                            null));
                    }
                    return true;
                }
                return false;
            };
        }


        private void Register(IPEndPoint address, IPEndPoint fallbackAddress)
        {
            ReportConnectFailure(() =>
            {
                Log.Debug("Attempting connection to [{0}]", address);

                _connectArgs = CreateSocketEventArgs(Self);
                _connectArgs.RemoteEndPoint = address;
                // we don't setup buffer here, it shouldn't be necessary just for connection
                if (!Socket.ConnectAsync(_connectArgs))
                    Self.Tell(IO.Tcp.SocketConnected.Instance);

                Become(Connecting(Tcp.Settings.FinishConnectRetries, _connectArgs, fallbackAddress));
            });
        }

        private Receive Connecting(int remainingFinishConnectRetries, SocketAsyncEventArgs args, IPEndPoint fallbackAddress)
        {
            return message =>
            {
                if (message is IO.Tcp.SocketConnected)
                {
                    if (args.SocketError == SocketError.Success)
                    {
                        if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                        Log.Debug("Connection established to [{0}]", _connect.RemoteAddress);

                        ReleaseConnectionSocketArgs();
                        AcquireSocketAsyncEventArgs();

                        CompleteConnect(_commander, _connect.Options);
                    }
                    else if (remainingFinishConnectRetries > 0 && fallbackAddress != null) // used only when we've resolved a DNS endpoint.
                    {
                        var self = Self;
                        var previousAddress = (IPEndPoint)args.RemoteEndPoint;
                        args.RemoteEndPoint = fallbackAddress;
                        Context.System.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(1), () =>
                        {
                            if (!Socket.ConnectAsync(args))
                                self.Tell(IO.Tcp.SocketConnected.Instance);
                        });
                        Context.Become(Connecting(remainingFinishConnectRetries - 1, args, previousAddress));
                    }
                    else if (remainingFinishConnectRetries > 0)
                    {
                        var self = Self;
                        Context.System.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(1), () =>
                        {
                            if (!Socket.ConnectAsync(args))
                                self.Tell(IO.Tcp.SocketConnected.Instance);
                        });
                        Context.Become(Connecting(remainingFinishConnectRetries - 1, args, null));
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
                    Log.Error("Connect timeout expired, could not establish connection to [{0}]", _connect.RemoteAddress);
                    Stop();
                    return true;
                }
                return false;
            };
        }
    }
}
