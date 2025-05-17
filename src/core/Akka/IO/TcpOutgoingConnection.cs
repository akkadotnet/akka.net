//-----------------------------------------------------------------------
// <copyright file="TcpOutgoingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// An actor handling the connection state machine for an outgoing connection
    /// to be established.
    /// </summary>
    internal sealed class TcpOutgoingConnection : TcpConnection
    {
        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;

        private SocketAsyncEventArgs _connectArgs;

        private readonly ConnectException _finishConnectNeverReturnedTrueException =
            new("Could not establish connection because finishConnect never returned true");

        public TcpOutgoingConnection(TcpExt tcp, IActorRef commander, Tcp.Connect connect)
            : base(
                (connect.TcpSettings ?? tcp.Settings),
                (connect.TcpSettings ?? tcp.Settings).OutgoingSocketForceIpv4
                    ? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false }
                    : new Socket(SocketType.Stream, ProtocolType.Tcp) { Blocking = false }, connect.PullMode)
        {
            _commander = commander;
            _connect = connect;

            foreach (var option in connect.Options)
            {
                option.BeforeConnect(Socket);
            }

            if (connect.LocalAddress != null)
                Socket.Bind(connect.LocalAddress);

            if (connect.Timeout.HasValue)
                Context.SetReceiveTimeout(connect.Timeout.Value); //Initiate connection timeout if supplied
        }

        private void ReleaseConnectionSocketArgs()
        {
            if (_connectArgs != null)
            {
                _connectArgs.UserToken = null;
                _connectArgs.AcceptSocket = null;

                try
                {
                    _connectArgs.SetBuffer(null, 0, 0);
                    _connectArgs.BufferList = null;
                }
                // it can be that for some reason socket is in use and haven't closed yet
                catch (InvalidOperationException)
                {
                }

                _connectArgs.Dispose();
                _connectArgs = null;
            }
        }

        private void Stop(Exception cause)
        {
            ReleaseConnectionSocketArgs();

            var failureEvent = _connect.FailureMessage.WithCause(cause);
            var closeInfo = CloseInformation.Single(_commander, failureEvent);
            StopWith(closeInfo);
            Context.Stop(Self);
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
                Log.Debug(e, "Could not establish connection to [{0}] due to {1}", _connect.RemoteAddress, e.Message);
                Stop(e);
            }
        }

        protected override void PreStart()
        {
            ReportConnectFailure(() =>
            {
                if (_connect.RemoteAddress is DnsEndPoint remoteAddress)
                {
                    Log.Debug("Resolving {0} before connecting", remoteAddress.Host);
                    var resolved = Dns.ResolveName(remoteAddress.Host, Context.System, Self);
                    if (resolved == null)
                        Become(() => Resolving(remoteAddress));
                    else if (resolved.Ipv4.Any() && resolved.Ipv6.Any()) // one of both families
                        Register(new IPEndPoint(resolved.Ipv4.First(), remoteAddress.Port),
                            new IPEndPoint(resolved.Ipv6.First(), remoteAddress.Port));
                    else // one or the other
                        Register(new IPEndPoint(resolved.Addr, remoteAddress.Port), null);
                }
                else if (_connect.RemoteAddress is IPEndPoint point)
                {
                    Register(point, null);
                }
                else
                    throw new NotSupportedException(
                        $"Couldn't connect to [{_connect.RemoteAddress}]: only IP and DNS-based endpoints are supported");
            });
        }

        protected override void PostStop()
        {
            // always try to release SocketAsyncEventArgs to avoid memory leaks
            ReleaseConnectionSocketArgs();

            base.PostStop();
        }

        private void Resolving(DnsEndPoint remoteAddress)
        {
            Receive<Dns.Resolved>(resolved =>
            {
                if (resolved.Ipv4.Any() && resolved.Ipv6.Any()) // multiple addresses
                {
                    ReportConnectFailure(() => Register(
                        new IPEndPoint(resolved.Ipv4.First(), remoteAddress.Port),
                        new IPEndPoint(resolved.Ipv6.First(), remoteAddress.Port)));
                }
                else // only one address family. No fallbacks.
                {
                    ReportConnectFailure(() => Register(
                        new IPEndPoint(resolved.Addr, remoteAddress.Port),
                        null));
                }
            });
        }

        private static SocketAsyncEventArgs CreateSocketEventArgs(IActorRef onCompleteNotificationsReceiver)
        {
            var args = new SocketAsyncEventArgs();
            args.UserToken = onCompleteNotificationsReceiver;
            args.Completed += (_, e) =>
            {
                var actorRef = e.UserToken as IActorRef;
                var completeMsg = ResolveMessage(e);
                actorRef?.Tell(completeMsg);
            };

            return args;

            Tcp.SocketCompleted ResolveMessage(SocketAsyncEventArgs e)
            {
                return e.LastOperation switch
                {
                    SocketAsyncOperation.Connect => IO.Tcp.SocketConnected.Instance,
                    _ => throw new NotSupportedException($"Socket operation {e.LastOperation} is not supported")
                };
            }
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

                Become(() => Connecting(Settings.FinishConnectRetries, _connectArgs, fallbackAddress));
            });
        }

        private void Connecting(int remainingFinishConnectRetries, SocketAsyncEventArgs args,
            IPEndPoint fallbackAddress)
        {
            Receive<Tcp.SocketConnected>(_ =>
            {
                if (args.SocketError == SocketError.Success)
                {
                    if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                    Log.Debug("Connection established to [{0}]", _connect.RemoteAddress);

                    ReleaseConnectionSocketArgs();

                    CompleteConnect(_commander, _connect.Options);
                }
                else
                    switch (remainingFinishConnectRetries)
                    {
                        // used only when we've resolved a DNS endpoint.
                        case > 0 when fallbackAddress != null:
                        {
                            var self = Self;
                            var previousAddress = (IPEndPoint)args.RemoteEndPoint;
                            args.RemoteEndPoint = fallbackAddress;
                            Context.System.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(1), () =>
                            {
                                if (!Socket.ConnectAsync(args))
                                    self.Tell(IO.Tcp.SocketConnected.Instance);
                            });
                            Become(() => Connecting(remainingFinishConnectRetries - 1, args, previousAddress));
                            break;
                        }
                        case > 0:
                        {
                            var self = Self;
                            Context.System.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(1), () =>
                            {
                                if (!Socket.ConnectAsync(args))
                                    self.Tell(IO.Tcp.SocketConnected.Instance);
                            });
                            Become(() => Connecting(remainingFinishConnectRetries - 1, args, null));
                            break;
                        }
                        default:
                            Log.Debug(
                                "Could not establish connection because finishConnect never returned true (consider increasing akka.io.tcp.finish-connect-retries)");
                            Stop(_finishConnectNeverReturnedTrueException);
                            break;
                    }
            });
            Receive<ReceiveTimeout>(_ =>
            {
                if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null); // Clear the timeout
                Log.Debug("Connect timeout expired, could not establish connection to [{0}]", _connect.RemoteAddress);
                Stop(new ConnectException($"Connect timeout of {_connect.Timeout} expired"));
            });
        }
    }

    [InternalApi]
    public class ConnectException : Exception
    {
        public ConnectException(string message)
            : base(message)
        {
        }
    }
}