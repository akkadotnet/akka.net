//-----------------------------------------------------------------------
// <copyright file="TcpOutgoingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;

#nullable enable

namespace Akka.IO
{
    /// <summary>
    /// An actor handling the connection state machine for an outgoing connection
    /// to be established.
    /// </summary>
    internal sealed class TcpOutgoingConnection : TcpConnection, IWithTimers
    {
        private const string RetryConnectTimerKey = "retry-connect";

        private readonly IActorRef _commander;
        private readonly Tcp.Connect _connect;

        private SocketAsyncEventArgs? _connectArgs;

        private readonly ConnectException _finishConnectNeverReturnedTrueException =
            new("Could not establish connection because finishConnect never returned true");

        public ITimerScheduler Timers { get; set; } = null!;

        /// <summary>
        /// Internal trigger used to re-attempt an outgoing connection from inside the
        /// actor's own message loop, so connect failures are reported as <see cref="Tcp.CommandFailed"/>
        /// instead of being swallowed by the scheduler thread.
        /// </summary>
        private sealed class RetryConnect
        {
            public static readonly RetryConnect Instance = new();
            private RetryConnect() { }
        }

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
                Context.SetReceiveTimeout(connect.Timeout.Value);
        }

        protected override ITransportConnection CreateTransport()
        {
            // NetworkStream requires a blocking socket; the socket was created
            // non-blocking for the SAEA connect phase — switch to blocking now.
            Socket.Blocking = true;

            var pipeBufferSize = ResolvePipeBufferSize(Settings, _connect.Options);
            var inputPipeOptions = new PipeOptions(
                pauseWriterThreshold: pipeBufferSize * 2,
                resumeWriterThreshold: pipeBufferSize,
                useSynchronizationContext: false);

            return new TcpTransportConnection(Socket, inputPipeOptions: inputPipeOptions);
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
                    // Pass DnsEndPoint directly to Socket.ConnectAsync — the runtime
                    // resolves DNS and tries all addresses (IPv4 + IPv6) until one connects.
                    // This is the only correct approach for dual-stack sockets where we
                    // don't know what address family the server is bound to.
                    Log.Debug("Connecting to DNS endpoint [{0}]", remoteAddress);
                    RegisterEndPoint(remoteAddress);
                }
                else if (_connect.RemoteAddress is IPEndPoint point)
                {
                    Register(point);
                }
                else
                    throw new NotSupportedException(
                        $"Couldn't connect to [{_connect.RemoteAddress}]: only IP and DNS-based endpoints are supported");
            });
        }

        protected override void PostStop()
        {
            ReleaseConnectionSocketArgs();
            base.PostStop();
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

        private void RegisterEndPoint(EndPoint address)
        {
            ReportConnectFailure(() =>
            {
                Log.Debug("Attempting connection to [{0}]", address);

                _connectArgs = CreateSocketEventArgs(Self);
                _connectArgs.RemoteEndPoint = address;
                if (!Socket.ConnectAsync(_connectArgs))
                    Self.Tell(IO.Tcp.SocketConnected.Instance);

                Become(() => Connecting(Settings.FinishConnectRetries, _connectArgs));
            });
        }

        private void Register(IPEndPoint address)
        {
            RegisterEndPoint(address);
        }

        private void Connecting(int remainingFinishConnectRetries, SocketAsyncEventArgs args)
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
                        case > 0:
                        {
                            ScheduleConnectRetry();
                            Become(() => Connecting(remainingFinishConnectRetries - 1, args));
                            break;
                        }
                        default:
                            Log.Debug(
                                "Could not establish connection because finishConnect never returned true (consider increasing akka.io.tcp.finish-connect-retries)");
                            Stop(_finishConnectNeverReturnedTrueException);
                            break;
                    }
            });
            Receive<RetryConnect>(_ =>
            {
                // Re-attempt the connection from within the actor's message loop so that any
                // exception (e.g. PlatformNotSupportedException on Linux when reusing a socket
                // after a failed connect attempt) is caught by ReportConnectFailure and reported
                // to the commander as Tcp.CommandFailed instead of being swallowed by the scheduler.
                ReportConnectFailure(() =>
                {
                    if (!Socket.ConnectAsync(args))
                        Self.Tell(IO.Tcp.SocketConnected.Instance);
                });
            });
            Receive<ReceiveTimeout>(_ =>
            {
                if (_connect.Timeout.HasValue) Context.SetReceiveTimeout(null);
                Log.Debug("Connect timeout expired, could not establish connection to [{0}]", _connect.RemoteAddress);
                Stop(new ConnectException($"Connect timeout of {_connect.Timeout} expired"));
            });
        }

        private void ScheduleConnectRetry()
            => Timers.StartSingleTimer(RetryConnectTimerKey, RetryConnect.Instance, TimeSpan.FromMilliseconds(1));
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
