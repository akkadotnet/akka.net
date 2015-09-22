//-----------------------------------------------------------------------
// <copyright file="TcpOutgoingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly IChannelRegistry _channelRegistry;
        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;

        public TcpOutgoingConnection(TcpExt tcp, IChannelRegistry channelRegistry, IActorRef commander, Tcp.Connect connect)
            : base(tcp, SocketChannel.Open().ConfigureBlocking(false), connect.PullMode)
        {
            _channelRegistry = channelRegistry;
            _commander = commander;
            _connect = connect;

            Context.Watch(commander);    // sign death pact

            connect.Options.ForEach(_ => _.BeforeConnect(Channel.Socket));
            if (connect.LocalAddress != null)
                Channel.Socket.Bind(connect.LocalAddress);
            channelRegistry.Register(Channel, SocketAsyncOperation.None, Self);
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

        protected override bool Receive(object message)
        {
            var registration = message as ChannelRegistration;
            if (registration != null)
            {
                ReportConnectFailure(() =>
                {
                    var remoteAddress = _connect.RemoteAddress as DnsEndPoint;
                    if (remoteAddress != null)
                    {
                        Log.Debug("Resolving {0} before connecting", remoteAddress.Host);
                        var resolved = Dns.ResolveName(remoteAddress.Host, Context.System, Self);
                        if(resolved == null) 
                            Become(Resolving(remoteAddress, registration));
                        else
                            Register(new IPEndPoint(resolved.Addr, remoteAddress.Port), registration);
                    }
                    else
                    {
                        Register(_connect.RemoteAddress, registration);
                    }
                });
                return true;
            }
            return false;
        }

        private Receive Resolving(DnsEndPoint remoteAddress, ChannelRegistration registration)
        {
            return message =>
            {
                var resolved = message as Dns.Resolved;
                if (resolved != null)
                {
                    ReportConnectFailure(() => Register(new IPEndPoint(resolved.Addr, remoteAddress.Port), registration));
                    return true;
                }
                return false;
            };
        }


        private void Register(EndPoint address, ChannelRegistration registration)
        {
            ReportConnectFailure(() =>
            {
                Log.Debug("Attempting connection to [{0}]", address);
                if (Channel.Connect(address))
                {
                    CompleteConnect(registration, _commander, _connect.Options);
                }
                else
                {
                    registration.EnableInterest(SocketAsyncOperation.Connect);
                    Become(Connecting(registration, Tcp.Settings.FinishConnectRetries));
                }
            });
        }

        private Receive Connecting(ChannelRegistration registration, int remainingFinishConnectRetries)
        {
            return message =>
            {
                if (message is SelectionHandler.ChannelConnectable)
                {
                    ReportConnectFailure(() =>
                    {
                        if (Channel.FinishConnect())
                        {
                            if(_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                            Log.Debug("Connection established to [{0}]", _connect.RemoteAddress);
                            CompleteConnect(registration, _commander, _connect.Options);
                        }
                        else
                        {
                            if (remainingFinishConnectRetries > 0)
                            {
                                var self = Self;
                                Context.System.Scheduler.Advanced.ScheduleOnce(1, () => _channelRegistry.Register(Channel, SocketAsyncOperation.Connect, self));
                                Context.Become(Connecting(registration, remainingFinishConnectRetries - 1));
                            }
                            else
                            {
                                Log.Debug("Could not establish connection because finishConnect never returned true (consider increasing akka.io.tcp.finish-connect-retries)");
                                Stop();
                            }
                        }
                    });
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
