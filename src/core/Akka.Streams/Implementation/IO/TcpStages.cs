using System;
using System.Collections.Immutable;
using System.Net;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Actor;
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

        private sealed class ConnectionSourceStageLogic : TimerGraphStageLogic
        {
            private const string BindShutdownTimer = "BindTimer";

            private readonly ConnectionSourceStage _stage;
            private readonly TaskCompletionSource<StreamTcp.ServerBinding> _bindingPromise;
            private readonly TaskCompletionSource<Unit> _unbindPromise = new TaskCompletionSource<Unit>();
            private IActorRef _listener;

            public ConnectionSourceStageLogic(Shape shape, ConnectionSourceStage stage, TaskCompletionSource<StreamTcp.ServerBinding> bindingPromise) : base(shape)
            {
                _stage = stage;
                _bindingPromise = bindingPromise;

                SetHandler(_stage._out, onPull: () =>
                {
                    // Ignore if still binding
                    _listener?.Tell(new Tcp.ResumeAccepting(1), StageActorRef);
                }, onDownstreamFinish: TryUnbind);
            }
            
            private StreamTcp.IncomingConnection ConnectionFor(Tcp.Connected connected, IActorRef connection)
            {
                _stage._connectionFlowsAwaitingInitialization.IncrementAndGet();

                var tcpFlow =
                    Flow.FromGraph(new IncomingConnectionStage(connection, connected.RemoteAddress, _stage._halfClose))
                    .Via(new Detacher<ByteString>()) // must read ahead for proper completions
                    .MapMaterializedValue(unit =>
                    {
                        _stage._connectionFlowsAwaitingInitialization.DecrementAndGet();
                        return unit;
                    });

                // FIXME: Previous code was wrong, must add new tests
                var handler = tcpFlow;
                if (_stage._idleTimeout.HasValue)
                    handler = tcpFlow.Join(BidiFlow.BidirectionalIdleTimeout<ByteString, ByteString>(_stage._idleTimeout.Value));

                return new StreamTcp.IncomingConnection(connected.LocalAddress, connected.RemoteAddress, handler);
            }

            private void TryUnbind()
            {
                if (_listener == null)
                    return;

                StageActorRef.Unwatch(_listener);
                SetKeepGoing(true);
                _listener.Tell(Tcp.Unbind.Instance, StageActorRef);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (BindShutdownTimer.Equals(timerKey))
                    CompleteStage(); // TODO need to manually shut down instead right?
            }

            public override void PreStart()
            {
                GetStageActorRef(Receive);
                _stage._tcpManager.Tell(new Tcp.Bind(StageActorRef, _stage._endpoint, _stage._backlog, _stage._options, pullMode: true), StageActorRef);
            }

            private void Receive(Tuple<IActorRef, object> args)
            {
                var sender = args.Item1;
                var msg = args.Item2;
                msg.Match()
                    .With<Tcp.Bound>(bound =>
                    {
                        _listener = sender;
                        StageActorRef.Watch(_listener);

                        if (IsAvailable(_stage._out))
                            _listener.Tell(new Tcp.ResumeAccepting(1), StageActorRef);

                        var target = StageActorRef;
                        _bindingPromise.TrySetResult(new StreamTcp.ServerBinding(bound.LocalAddress, () =>
                        {
                            target.Tell(Tcp.Unbind.Instance, StageActorRef);
                            return _unbindPromise.Task;
                        }));
                    })
                    .With<Tcp.CommandFailed>(() =>
                    {
                        var ex = BindFailedException.Instance;
                        _bindingPromise.TrySetException(ex);
                        _unbindPromise.TrySetResult(Unit.Instance);
                        FailStage(ex);
                    })
                    .With<Tcp.Connected>(c =>
                    {
                        Push(_stage._out, ConnectionFor(c, sender));
                    })
                    .With<Tcp.Unbind>(() =>
                    {
                        if (!IsClosed(_stage._out) && _listener != null)
                            TryUnbind();
                    })
                    .With<Tcp.Unbound>(() => // If we're unbound then just shut down
                    {
                        if (_stage._connectionFlowsAwaitingInitialization.Current == 0)
                            CompleteStage();
                        else
                            ScheduleOnce(BindShutdownTimer, _stage._bindShutdownTimeout);
                    })
                    .With<Terminated>(terminated =>
                    {
                        if (Equals(terminated.ActorRef, _listener))
                            FailStage(new IllegalStateException("IO Listener actor terminated unexpectedly"));
                    });
            }

            public override void PostStop()
            {
                _unbindPromise.TrySetResult(Unit.Instance);
                _bindingPromise.TrySetException(
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
        private readonly AtomicCounterLong _connectionFlowsAwaitingInitialization = new AtomicCounterLong();
        private readonly Outlet<StreamTcp.IncomingConnection> _out = new Outlet<StreamTcp.IncomingConnection>("IncomingConnections.out");

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

        public override SourceShape<StreamTcp.IncomingConnection> Shape { get; }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("ConnectionSource");

        // TODO: Timeout on bind
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<StreamTcp.ServerBinding> materialized)
        {
            var bindingPromise = new TaskCompletionSource<StreamTcp.ServerBinding>();
            var logic = new ConnectionSourceStageLogic(Shape, this, bindingPromise);
            materialized = bindingPromise.Task;
            return logic;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class IncomingConnectionStage : GraphStage<FlowShape<ByteString, ByteString>>
    {
        private readonly IActorRef _connection;
        private readonly EndPoint _remoteAddress;
        private readonly bool _halfClose;
        private readonly AtomicBoolean _hasBeenCreated = new AtomicBoolean();
        private readonly Inlet<ByteString> _bytesIn = new Inlet<ByteString>("IncomingTCP.in");
        private readonly Outlet<ByteString> _bytesOut = new Outlet<ByteString>("IncomingTCP.out");

        public IncomingConnectionStage(IActorRef connection, EndPoint remoteAddress, bool halfClose)
        {
            _connection = connection;
            _remoteAddress = remoteAddress;
            _halfClose = halfClose;
            Shape = new FlowShape<ByteString, ByteString>(_bytesIn, _bytesOut);
        }

        public override FlowShape<ByteString, ByteString> Shape { get; }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("IncomingConnection");

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            if (_hasBeenCreated.Value)
                throw new IllegalStateException("Cannot materialize an incoming connection Flow twice.");
            _hasBeenCreated.Value = true;

            return new TcpConnectionStage.TcpStreamLogic(Shape, new TcpConnectionStage.Inbound(_connection, _halfClose));
        }

        public override string ToString() => $"TCP-from {_remoteAddress}";
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

        internal interface ITcpRole
        {
            bool HalfClose { get; }
        }

        internal struct Outbound : ITcpRole
        {
            public Outbound(IActorRef manager, Tcp.Connect connectCmd, TaskCompletionSource<EndPoint> localAddressPromise, bool halfClose)
            {
                Manager = manager;
                ConnectCmd = connectCmd;
                LocalAddressPromise = localAddressPromise;
                HalfClose = halfClose;
            }

            public readonly IActorRef Manager;

            public readonly Tcp.Connect ConnectCmd;

            public readonly TaskCompletionSource<EndPoint> LocalAddressPromise;

            public bool HalfClose { get; }
        }

        internal struct Inbound : ITcpRole
        {
            public Inbound(IActorRef connection, bool halfClose)
            {
                Connection = connection;
                HalfClose = halfClose;
            }

            public readonly IActorRef Connection;

            public bool HalfClose { get; }
        }

        internal sealed class TcpStreamLogic : GraphStageLogic
        {
            private readonly ITcpRole _role;
            private readonly Inlet<ByteString> _bytesIn;
            private readonly Outlet<ByteString> _bytesOut;
            private IActorRef _connection;
            private readonly OutHandler _readHandler;

            public TcpStreamLogic(FlowShape<ByteString, ByteString> shape, ITcpRole role) : base(shape)
            {
                _role = role;
                _bytesIn = shape.Inlet;
                _bytesOut = shape.Outlet;

                _readHandler = new LambdaOutHandler(
                    onPull: () => _connection.Tell(Tcp.ResumeReading.Instance, StageActorRef),
                    onDownstreamFinish: () =>
                    {
                        if (!IsClosed(_bytesIn))
                            _connection.Tell(Tcp.ResumeReading.Instance, StageActorRef);
                        else
                        {
                            _connection.Tell(Tcp.Abort.Instance, StageActorRef);
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
                        _connection.Tell(Tcp.Write.Create(elem, WriteAck.Instance), StageActorRef);
                    },
                    onUpstreamFinish: () =>
                    {
                        // Reading has stopped before, either because of cancel, or PeerClosed, so just Close now
                        // (or half-close is turned off)
                        if (IsClosed(_bytesOut) || !_role.HalfClose)
                            _connection.Tell(Tcp.Close.Instance, StageActorRef);
                        // We still read, so we only close the write side
                        else if (_connection != null)
                            _connection.Tell(Tcp.ConfirmedClose.Instance, StageActorRef);
                        else
                            CompleteStage();
                    },
                    onUpstreamFailure: ex =>
                    {
                        if (_connection != null)
                        {
                            if (Interpreter.Log.IsDebugEnabled)
                                Interpreter.Log.Debug(
                                    $"Aborting tcp connection because of upstream failure: {ex.Message}\n{ex.StackTrace}");

                            _connection.Tell(Tcp.Abort.Instance, StageActorRef);
                        }
                        else
                            FailStage(ex);
                    });
            }

            public override void PreStart()
            {
                SetKeepGoing(true);

                if (_role is Inbound)
                {
                    var inbound = (Inbound)_role;
                    SetHandler(_bytesOut, _readHandler);
                    _connection = inbound.Connection;
                    GetStageActorRef(Connected).Watch(_connection);
                    _connection.Tell(new Tcp.Register(StageActorRef, keepOpenonPeerClosed: true, useResumeWriting: false), StageActorRef);
                    Pull(_bytesIn);
                }
                else
                {
                    var outbound = (Outbound)_role;
                    GetStageActorRef(Connecting(outbound)).Watch(outbound.Manager);
                    outbound.Manager.Tell(outbound.ConnectCmd, StageActorRef);
                }
            }

            public override void PostStop()
            {
                if (_role is Outbound)
                {
                    var outbound = (Outbound) _role;
                    // Fail if has not been completed with an address eariler
                    outbound.LocalAddressPromise.TrySetException(new StreamTcpException("Connection failed"));
                }
            }

            private StageActorRef.Receive Connecting(Outbound outbound)
            {
                return args =>
                {
                    var sender = args.Item1;
                    var msg = args.Item2;

                    msg.Match()
                        .With<Terminated>(() => FailStage(new StreamTcpException("The IO manager actor (TCP) has terminated. Stopping now.")))
                        .With<Tcp.CommandFailed>(failed => FailStage(new StreamTcpException($"Tcp command {failed.Cmd} failed")))
                        .With<Tcp.Connected>(c =>
                        {
                            ((Outbound)_role).LocalAddressPromise.TrySetResult(c.LocalAddress);
                            _connection = sender;
                            SetHandler(_bytesOut, _readHandler);
                            StageActorRef.Unwatch(outbound.Manager);
                            StageActorRef.Become(Connected);
                            StageActorRef.Watch(_connection);
                            _connection.Tell(new Tcp.Register(StageActorRef, keepOpenonPeerClosed: true, useResumeWriting: false), StageActorRef);

                            if (IsAvailable(_bytesOut))
                                _connection.Tell(Tcp.ResumeReading.Instance, StageActorRef);

                            Pull(_bytesIn);
                        });
                };

            }

            private void Connected(Tuple<IActorRef, object> args)
            {
                var msg = args.Item2;

                msg.Match()
                    .With<Terminated>(() => FailStage(new StreamTcpException("The connection actor has terminated. Stopping now.")))
                    .With<Tcp.CommandFailed>(failed => FailStage(new StreamTcpException($"Tcp command {failed.Cmd} failed")))
                    .With<Tcp.ErrorClosed>(cause => FailStage(new StreamTcpException($"The connection closed with error: {cause}")))
                    .With<Tcp.Aborted>(() => FailStage(new StreamTcpException("The connection has been aborted")))
                    .With<Tcp.Closed>(CompleteStage)
                    .With<Tcp.ConfirmedClosed>(CompleteStage)
                    .With<Tcp.PeerClosed>(() => Complete(_bytesOut))
                    .With<Tcp.Received>(received =>
                    {
                        // Keep on reading even when closed. There is no "close-read-side" in TCP
                        if (IsClosed(_bytesOut))
                            _connection.Tell(Tcp.ResumeReading.Instance, StageActorRef);
                        else
                            Push(_bytesOut, received.Data);
                    })
                    .With<WriteAck>(() =>
                    {
                        if (!IsClosed(_bytesIn))
                            Pull(_bytesIn);
                    });
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


        public OutgoingConnectionStage(IActorRef tcpManager, EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null)
        {
            _tcpManager = tcpManager;
            _remoteAddress = remoteAddress;
            _localAddress = localAddress;
            _options = options;
            _halfClose = halfClose;
            _connectionTimeout = connectionTimeout;
            Shape  = new FlowShape<ByteString, ByteString>(_bytesIn, _bytesOut);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("OutgoingConnection");

        public override FlowShape<ByteString, ByteString> Shape { get; }

        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<StreamTcp.OutgoingConnection> materialized)
        {
            var localAddressPromise = new TaskCompletionSource<EndPoint>();
            var outgoingConnectionPromise = new TaskCompletionSource<StreamTcp.OutgoingConnection>();
            localAddressPromise.Task.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                        outgoingConnectionPromise.TrySetCanceled();
                    else if (t.IsFaulted)
                        outgoingConnectionPromise.TrySetException(t.Exception);
                    else
                        outgoingConnectionPromise.TrySetResult(new StreamTcp.OutgoingConnection(_remoteAddress, t.Result));
                }, TaskContinuationOptions.AttachedToParent);

            var logic = new TcpConnectionStage.TcpStreamLogic(Shape,
                new TcpConnectionStage.Outbound(_tcpManager,
                    new Tcp.Connect(_remoteAddress, _localAddress, _options, _connectionTimeout, pullMode: true),
                    localAddressPromise, _halfClose));
            materialized = outgoingConnectionPromise.Task;

            return logic;
        }

        public override string ToString() => $"TCP-to {_remoteAddress}";
    }
}
