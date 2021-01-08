//-----------------------------------------------------------------------
// <copyright file="TcpStages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;
using StreamTcp = Akka.Streams.Dsl.Tcp;
using Tcp = Akka.IO.Tcp;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ConnectionSourceStage : GraphStageWithMaterializedValue<SourceShape<StreamTcp.IncomingConnection>, Task<StreamTcp.ServerBinding>>
    {
        #region internal classes

        private sealed class ConnectionSourceStageLogic : TimerGraphStageLogic, IOutHandler
        {
            private const string BindShutdownTimer = "BindTimer";

            private readonly AtomicCounterLong _connectionFlowsAwaitingInitialization = new AtomicCounterLong();
            private readonly ConnectionSourceStage _stage;
            private IActorRef _listener;
            private readonly TaskCompletionSource<StreamTcp.ServerBinding> _bindingPromise;
            private readonly TaskCompletionSource<NotUsed> _unbindPromise = new TaskCompletionSource<NotUsed>();
            private bool _unbindStarted = false;

            public ConnectionSourceStageLogic(Shape shape, ConnectionSourceStage stage, TaskCompletionSource<StreamTcp.ServerBinding> bindingPromise)
                : base(shape)
            {
                _stage = stage;
                _bindingPromise = bindingPromise;

                SetHandler(_stage._out, this);
            }

            public void OnPull()
            {
                // Ignore if still binding
                _listener?.Tell(new Tcp.ResumeAccepting(1), StageActor.Ref);
            }

            public void OnDownstreamFinish() => TryUnbind();

            private StreamTcp.IncomingConnection ConnectionFor(Tcp.Connected connected, IActorRef connection)
            {
                _connectionFlowsAwaitingInitialization.IncrementAndGet();

                var tcpFlow =
                    Flow.FromGraph(new IncomingConnectionStage(connection, connected.RemoteAddress, _stage._halfClose))
                    .Via(new Detacher<ByteString>()) // must read ahead for proper completions
                    .MapMaterializedValue(unit =>
                    {
                        _connectionFlowsAwaitingInitialization.DecrementAndGet();
                        return unit;
                    });

                // FIXME: Previous code was wrong, must add new tests
                var handler = tcpFlow;
                if (_stage._idleTimeout.HasValue)
                    handler = tcpFlow.Join(TcpIdleTimeout.Create(_stage._idleTimeout.Value, connected.RemoteAddress));

                return new StreamTcp.IncomingConnection(connected.LocalAddress, connected.RemoteAddress, handler);
            }

            private void TryUnbind()
            {
                if (_listener != null && !_unbindStarted)
                {
                    _unbindStarted = true;
                    SetKeepGoing(true);
                    _listener.Tell(Tcp.Unbind.Instance, StageActor.Ref);
                }
            }

            private void UnbindCompleted()
            {
                StageActor.Unwatch(_listener);
                if (_connectionFlowsAwaitingInitialization.Current == 0)
                    CompleteStage();
                else
                    ScheduleOnce(BindShutdownTimer, _stage._bindShutdownTimeout);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (Equals(BindShutdownTimer, timerKey))
                    CompleteStage(); // TODO need to manually shut down instead right?
            }

            public override void PreStart()
            {
                GetStageActor(Receive);
                _stage._tcpManager.Tell(new Tcp.Bind(StageActor.Ref, _stage._endpoint, _stage._backlog, _stage._options, pullMode: true), StageActor.Ref);
            }

            private void Receive((IActorRef, object) args)
            {
                var sender = args.Item1;
                var msg = args.Item2;
                if (msg is Tcp.Bound)
                {
                    var bound = (Tcp.Bound)msg;
                    _listener = sender;
                    StageActor.Watch(_listener);

                    if (IsAvailable(_stage._out))
                        _listener.Tell(new Tcp.ResumeAccepting(1), StageActor.Ref);

                    var thisStage = StageActor.Ref;
                    var binding = new StreamTcp.ServerBinding(bound.LocalAddress, () =>
                    {
                        // Beware, sender must be explicit since stageActor.ref will be invalid to access after the stage stopped
                        thisStage.Tell(Tcp.Unbind.Instance, thisStage);
                        return _unbindPromise.Task;
                    });

                    _bindingPromise.NonBlockingTrySetResult(binding);
                }
                else if (msg is Tcp.CommandFailed)
                {
                    var ex = BindFailedException.Instance;
                    _bindingPromise.NonBlockingTrySetException(ex);
                    _unbindPromise.TrySetResult(NotUsed.Instance);
                    FailStage(ex);
                }
                else if (msg is Tcp.Connected)
                {
                    var connected = (Tcp.Connected)msg;
                    Push(_stage._out, ConnectionFor(connected, sender));
                }
                else if (msg is Tcp.Unbind)
                {
                    if (!IsClosed(_stage._out) && !ReferenceEquals(_listener, null))
                        TryUnbind();
                }
                else if (msg is Tcp.Unbound)
                {
                    UnbindCompleted();
                }
                else if (msg is Terminated)
                {
                    if (_unbindStarted) UnbindCompleted();
                    else FailStage(new IllegalStateException("IO Listener actor terminated unexpectedly"));
                }
            }

            public override void PostStop()
            {
                _unbindPromise.TrySetResult(NotUsed.Instance);
                _bindingPromise.NonBlockingTrySetException(
                    new NoSuchElementException("Binding was unbound before it was completely finished"));
            }
        }

        #endregion

        private readonly IActorRef _tcpManager;
        private readonly EndPoint _endpoint;
        private readonly int _backlog;
        private readonly IImmutableList<Inet.SocketOption> _options;
        private readonly bool _halfClose;
        private readonly TimeSpan? _idleTimeout;
        private readonly TimeSpan _bindShutdownTimeout;
        private readonly Outlet<StreamTcp.IncomingConnection> _out = new Outlet<StreamTcp.IncomingConnection>("IncomingConnections.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcpManager">TBD</param>
        /// <param name="endpoint">TBD</param>
        /// <param name="backlog">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="halfClose">TBD</param>
        /// <param name="idleTimeout">TBD</param>
        /// <param name="bindShutdownTimeout">TBD</param>
        public ConnectionSourceStage(IActorRef tcpManager, EndPoint endpoint, int backlog,
            IImmutableList<Inet.SocketOption> options, bool halfClose, TimeSpan? idleTimeout,
            TimeSpan bindShutdownTimeout)
        {
            _tcpManager = tcpManager;
            _endpoint = endpoint;
            _backlog = backlog;
            _options = options;
            _halfClose = halfClose;
            _idleTimeout = idleTimeout;
            _bindShutdownTimeout = bindShutdownTimeout;
            Shape = new SourceShape<StreamTcp.IncomingConnection>(_out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<StreamTcp.IncomingConnection> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("ConnectionSource");

        // TODO: Timeout on bind
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<StreamTcp.ServerBinding>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var bindingPromise = TaskEx.NonBlockingTaskCompletionSource<StreamTcp.ServerBinding>();
            var logic = new ConnectionSourceStageLogic(Shape, this, bindingPromise);
            return new LogicAndMaterializedValue<Task<StreamTcp.ServerBinding>>(logic, bindingPromise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public class IncomingConnectionStage : GraphStage<FlowShape<ByteString, ByteString>>
    {
        private readonly IActorRef _connection;
        private readonly EndPoint _remoteAddress;
        private readonly bool _halfClose;
        private readonly AtomicBoolean _hasBeenCreated = new AtomicBoolean();
        private readonly Inlet<ByteString> _bytesIn = new Inlet<ByteString>("IncomingTCP.in");
        private readonly Outlet<ByteString> _bytesOut = new Outlet<ByteString>("IncomingTCP.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="halfClose">TBD</param>
        public IncomingConnectionStage(IActorRef connection, EndPoint remoteAddress, bool halfClose)
        {
            _connection = connection;
            _remoteAddress = remoteAddress;
            _halfClose = halfClose;
            Shape = new FlowShape<ByteString, ByteString>(_bytesIn, _bytesOut);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<ByteString, ByteString> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("IncomingConnection");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            if (_hasBeenCreated.Value)
                throw new IllegalStateException("Cannot materialize an incoming connection Flow twice.");
            _hasBeenCreated.Value = true;

            return new TcpConnectionStage.TcpStreamLogic(Shape, new TcpConnectionStage.Inbound(_connection, _halfClose), _remoteAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"TCP-from({_remoteAddress})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class TcpConnectionStage
    {
        private class WriteAck : Tcp.Event
        {
            public static readonly WriteAck Instance = new WriteAck();

            private WriteAck()
            {

            }

        }

        /// <summary>
        /// TBD
        /// </summary>
        internal interface ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            bool HalfClose { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal struct Outbound : ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="manager">TBD</param>
            /// <param name="connectCmd">TBD</param>
            /// <param name="localAddressPromise">TBD</param>
            /// <param name="halfClose">TBD</param>
            public Outbound(IActorRef manager, Tcp.Connect connectCmd, TaskCompletionSource<EndPoint> localAddressPromise, bool halfClose)
            {
                Manager = manager;
                ConnectCmd = connectCmd;
                LocalAddressPromise = localAddressPromise;
                HalfClose = halfClose;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Manager;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly Tcp.Connect ConnectCmd;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly TaskCompletionSource<EndPoint> LocalAddressPromise;

            /// <summary>
            /// TBD
            /// </summary>
            public bool HalfClose { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal struct Inbound : ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="connection">TBD</param>
            /// <param name="halfClose">TBD</param>
            public Inbound(IActorRef connection, bool halfClose)
            {
                Connection = connection;
                HalfClose = halfClose;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Connection;

            /// <summary>
            /// TBD
            /// </summary>
            public bool HalfClose { get; }
        }

        /// <summary>
        /// This is a *non-detached* design, i.e. this does not prefetch itself any of the inputs. It relies on downstream
        /// stages to provide the necessary prefetch on `bytesOut` and the framework to do the proper prefetch in the buffer
        /// backing `bytesIn`. If prefetch on `bytesOut` is required (i.e. user stages cannot be trusted) then it is better
        /// to attach an extra, fused buffer to the end of this flow. Keeping this stage non-detached makes it much simpler and
        /// easier to maintain and understand.
        /// </summary>
        internal sealed class TcpStreamLogic : GraphStageLogic
        {
            private readonly ITcpRole _role;
            private readonly EndPoint _remoteAddress;
            private readonly Inlet<ByteString> _bytesIn;
            private readonly Outlet<ByteString> _bytesOut;
            private IActorRef _connection;
            private readonly OutHandler _readHandler;
            
            public TcpStreamLogic(FlowShape<ByteString, ByteString> shape, ITcpRole role, EndPoint remoteAddress) : base(shape)
            {
                _role = role;
                _remoteAddress = remoteAddress;
                _bytesIn = shape.Inlet;
                _bytesOut = shape.Outlet;

                _readHandler = new LambdaOutHandler(
                    onPull: () => _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref),
                    onDownstreamFinish: () =>
                    {
                        if (!IsClosed(_bytesIn))
                            _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);
                        else
                        {
                            _connection.Tell(Tcp.Abort.Instance, StageActor.Ref);
                            CompleteStage();
                        }
                    });

                // No reading until role have been decided
                SetHandler(_bytesOut, onPull: DoNothing);
                SetHandler(_bytesIn,
                    onPush: () =>
                    {
                        var elem = Grab(_bytesIn);
                        ReactiveStreamsCompliance.RequireNonNullElement(elem);
                        _connection.Tell(Tcp.Write.Create(elem, WriteAck.Instance), StageActor.Ref);
                    },
                    onUpstreamFinish: () =>
                    {
                        // Reading has stopped before, either because of cancel, or PeerClosed, so just Close now
                        // (or half-close is turned off)
                        if (IsClosed(_bytesOut) || !_role.HalfClose)
                            _connection.Tell(Tcp.Close.Instance, StageActor.Ref);
                        // We still read, so we only close the write side
                        else if (_connection != null)
                            _connection.Tell(Tcp.ConfirmedClose.Instance, StageActor.Ref);
                        else
                            CompleteStage();
                    },
                    onUpstreamFailure: ex =>
                    {
                        if (_connection != null)
                        {
                            if (Interpreter.Log.IsDebugEnabled)
                                Interpreter.Log.Debug(
                                    $"Aborting tcp connection to {_remoteAddress} because of upstream failure: {ex.Message}\n{ex.StackTrace}");
                            _connection.Tell(Tcp.Abort.Instance, StageActor.Ref);
                        }
                        else
                            FailStage(ex);
                    });
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void PreStart()
            {
                SetKeepGoing(true);

                if (_role is Inbound)
                {
                    var inbound = (Inbound)_role;
                    SetHandler(_bytesOut, _readHandler);
                    _connection = inbound.Connection;
                    GetStageActor(Connected).Watch(_connection);
                    _connection.Tell(new Tcp.Register(StageActor.Ref, keepOpenOnPeerClosed: true, useResumeWriting: false), StageActor.Ref);
                    Pull(_bytesIn);
                }
                else
                {
                    var outbound = (Outbound)_role;
                    GetStageActor(Connecting(outbound)).Watch(outbound.Manager);
                    outbound.Manager.Tell(outbound.ConnectCmd, StageActor.Ref);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void PostStop()
            {
                if (_role is Outbound)
                {
                    var outbound = (Outbound)_role;
                    // Fail if has not been completed with an address earlier
                    outbound.LocalAddressPromise.TrySetException(new StreamTcpException("Connection failed"));
                }
            }

            private StageActorRef.Receive Connecting(Outbound outbound)
            {
                return args =>
                {
                    var sender = args.Item1;
                    var msg = args.Item2;

                    if (msg is Terminated)
                        FailStage(new StreamTcpException("The IO manager actor (TCP) has terminated. Stopping now."));
                    else if (msg is Tcp.CommandFailed)
                        FailStage(new StreamTcpException($"Tcp command {((Tcp.CommandFailed)msg).Cmd} failed"));
                    else if (msg is Tcp.Connected)
                    {
                        var connected = (Tcp.Connected)msg;

                        ((Outbound)_role).LocalAddressPromise.TrySetResult(connected.LocalAddress);
                        _connection = sender;
                        SetHandler(_bytesOut, _readHandler);
                        StageActor.Unwatch(outbound.Manager);
                        StageActor.Become(Connected);
                        StageActor.Watch(_connection);
                        _connection.Tell(new Tcp.Register(StageActor.Ref, keepOpenOnPeerClosed: true, useResumeWriting: false), StageActor.Ref);

                        if (IsAvailable(_bytesOut))
                            _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);

                        Pull(_bytesIn);
                    }
                };

            }

            private void Connected((IActorRef, object) args)
            {
                var msg = args.Item2;

                if (msg is Terminated) FailStage(new StreamTcpException("The connection actor has terminated. Stopping now."));
                else if (msg is Tcp.CommandFailed) FailStage(new StreamTcpException($"Tcp command {((Tcp.CommandFailed)msg).Cmd} failed"));
                else if (msg is Tcp.ErrorClosed) FailStage(new StreamTcpException($"The connection closed with error: {((Tcp.ErrorClosed)msg).Cause}"));
                else if (msg is Tcp.Aborted) FailStage(new StreamTcpException("The connection has been aborted"));
                else if (msg is Tcp.Closed) CompleteStage();
                else if (msg is Tcp.ConfirmedClosed) CompleteStage();
                else if (msg is Tcp.PeerClosed) Complete(_bytesOut);
                else if (msg is Tcp.Received)
                {
                    var received = (Tcp.Received)msg;
                    // Keep on reading even when closed. There is no "close-read-side" in TCP
                    if (IsClosed(_bytesOut)) _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);
                    else Push(_bytesOut, received.Data);
                }
                else if (msg is WriteAck)
                {
                    if (!IsClosed(_bytesIn)) Pull(_bytesIn);
                }
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OutgoingConnectionStage :
        GraphStageWithMaterializedValue<FlowShape<ByteString, ByteString>, Task<StreamTcp.OutgoingConnection>>
    {
        private readonly IActorRef _tcpManager;
        private readonly EndPoint _remoteAddress;
        private readonly EndPoint _localAddress;
        private readonly IImmutableList<Inet.SocketOption> _options;
        private readonly bool _halfClose;
        private readonly TimeSpan? _connectionTimeout;
        private readonly Inlet<ByteString> _bytesIn = new Inlet<ByteString>("IncomingTCP.in");
        private readonly Outlet<ByteString> _bytesOut = new Outlet<ByteString>("IncomingTCP.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcpManager">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="halfClose">TBD</param>
        /// <param name="connectionTimeout">TBD</param>
        public OutgoingConnectionStage(IActorRef tcpManager, EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null)
        {
            _tcpManager = tcpManager;
            _remoteAddress = remoteAddress;
            _localAddress = localAddress;
            _options = options;
            _halfClose = halfClose;
            _connectionTimeout = connectionTimeout;
            Shape = new FlowShape<ByteString, ByteString>(_bytesIn, _bytesOut);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("OutgoingConnection");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<ByteString, ByteString> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var localAddressPromise = new TaskCompletionSource<EndPoint>();
            var outgoingConnectionPromise = new TaskCompletionSource<StreamTcp.OutgoingConnection>();
            localAddressPromise.Task.ContinueWith(t =>
                {
                    if (t.IsCanceled) outgoingConnectionPromise.TrySetCanceled();
                    else if (t.IsFaulted) outgoingConnectionPromise.TrySetException(t.Exception);
                    else outgoingConnectionPromise.TrySetResult(new StreamTcp.OutgoingConnection(_remoteAddress, t.Result));
                }, TaskContinuationOptions.AttachedToParent);

            var logic = new TcpConnectionStage.TcpStreamLogic(Shape, new TcpConnectionStage.Outbound(_tcpManager, new Tcp.Connect(_remoteAddress, _localAddress, _options, _connectionTimeout, pullMode: true), localAddressPromise, _halfClose), _remoteAddress);

            return new LogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>>(logic, outgoingConnectionPromise.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"TCP-to({_remoteAddress})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class TcpIdleTimeout
    {
        public static BidiFlow<ByteString, ByteString, ByteString, ByteString, NotUsed> Create(TimeSpan idleTimeout, EndPoint remoteAddress = null)
        {
            var connectionString = remoteAddress == null ? "" : $" on connection to [{remoteAddress}]";

            var idleException = new TcpIdleTimeoutException(
                $"TCP idle-timeout encountered{connectionString}, no bytes passed in the last {idleTimeout}",
                idleTimeout);

            var toNetTimeout = BidiFlow.FromFlows(
                Flow.Create<ByteString>().SelectError(e => e is TimeoutException ? idleException : e),
                Flow.Create<ByteString>());

            var fromNetTimeout = toNetTimeout.Reversed(); // now the bottom flow transforms the exception, the top one doesn't (since that one is "fromNet") 

            return fromNetTimeout.Atop(BidiFlow.BidirectionalIdleTimeout<ByteString, ByteString>(idleTimeout))
                .Atop(toNetTimeout);
        }
    }
}
