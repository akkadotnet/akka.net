//-----------------------------------------------------------------------
// <copyright file="Controller.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using DotNetty.Transport.Channels;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// This controls test execution by managing barriers (delegated to
    /// <see cref="BarrierCoordinator"/>, its child) and allowing
    /// network and other failures to be injected at the test nodes.
    /// 
    /// INTERNAL API.
    /// </summary>
    internal class Controller : UntypedActor, ILogReceive, IWithUnboundedStash
    {
        public sealed class ClientDisconnected : IDeadLetterSuppression
        {
            private readonly RoleName _name;

            public ClientDisconnected(RoleName name)
            {
                _name = name;
            }

            public RoleName Name
            {
                get { return _name; }
            }

            private bool Equals(ClientDisconnected other)
            {
                return Equals(_name, other._name);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is ClientDisconnected disconnected && Equals(disconnected);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return (_name != null ? _name.GetHashCode() : 0);
            }

            /// <summary>
            /// Compares two specified <see cref="ClientDisconnected"/> for equality.
            /// </summary>
            /// <param name="left">The first <see cref="ClientDisconnected"/> used for comparison</param>
            /// <param name="right">The second <see cref="ClientDisconnected"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="ClientDisconnected"/> are equal; otherwise <c>false</c></returns>
            public static bool operator ==(ClientDisconnected left, ClientDisconnected right)
            {
                return Equals(left, right);
            }

            /// <summary>
            /// Compares two specified <see cref="ClientDisconnected"/> for inequality.
            /// </summary>
            /// <param name="left">The first <see cref="ClientDisconnected"/> used for comparison</param>
            /// <param name="right">The second <see cref="ClientDisconnected"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="ClientDisconnected"/> are not equal; otherwise <c>false</c></returns>
            public static bool operator !=(ClientDisconnected left, ClientDisconnected right)
            {
                return !Equals(left, right);
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"{GetType()}: {Name}";
            }
        }

        /// <summary>
        /// This exception is thrown when a client has disconnected.
        /// </summary>
        public class ClientDisconnectedException : AkkaException
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ClientDisconnectedException"/> class.
            /// </summary>
            /// <param name="message">The message that describes the error.</param>
            public ClientDisconnectedException(string message) : base(message){}

            /// <summary>
            /// Initializes a new instance of the <see cref="ClientDisconnectedException"/> class.
            /// </summary>
            /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
            /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
            protected ClientDisconnectedException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
        }

        public class GetNodes
        {
            private GetNodes() { }
            public static GetNodes Instance { get; } = new();
        }

        public class GetSockAddr
        {
            private GetSockAddr() { }
            public static GetSockAddr Instance { get; } = new();
        }

        /// <summary>
        /// Marker interface for working with <see cref="BarrierCoordinator"/>
        /// </summary>
        internal interface IHaveNodeInfo
        {
            NodeInfo Node { get; }
        }

        internal sealed class NodeInfo : IEquatable<NodeInfo>
        {
            public NodeInfo(RoleName name, Address addr, IActorRef fsm)
            {
                Name = name;
                Addr = addr;
                FSM = fsm;
            }

            public RoleName Name { get; }

            public Address Addr { get; }

            public IActorRef FSM { get; }

            public bool Equals(NodeInfo other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Name, other.Name) && Equals(Addr, other.Addr) && Equals(FSM, other.FSM);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is NodeInfo node && Equals(node);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (Name != null ? Name.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (Addr != null ? Addr.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (FSM != null ? FSM.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public override string ToString() => $"NodeInfo({Name}, {Addr})";

            /// <summary>
            /// Compares two specified <see cref="NodeInfo"/> for equality.
            /// </summary>
            /// <param name="left">The first <see cref="NodeInfo"/> used for comparison</param>
            /// <param name="right">The second <see cref="NodeInfo"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="NodeInfo"/> are equal; otherwise <c>false</c></returns>
            public static bool operator ==(NodeInfo left, NodeInfo right) => Equals(left, right);

            /// <summary>
            /// Compares two specified <see cref="NodeInfo"/> for inequality.
            /// </summary>
            /// <param name="left">The first <see cref="NodeInfo"/> used for comparison</param>
            /// <param name="right">The second <see cref="NodeInfo"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="NodeInfo"/> are not equal; otherwise <c>false</c></returns>
            public static bool operator !=(NodeInfo left, NodeInfo right) => !Equals(left, right);
        }

        public sealed class CreateServerFSM : INoSerializationVerificationNeeded
        {
            public CreateServerFSM(IChannel channel)
            {
                Channel = channel;
            }

            public IChannel Channel { get; private set; }
        }

        /// <summary>
        /// Carries a successful bind from <see cref="PreStart"/> back into the mailbox.
        /// </summary>
        private sealed class Bound : INoSerializationVerificationNeeded
        {
            public Bound(IChannel channel)
            {
                Channel = channel;
            }

            public IChannel Channel { get; }
        }

        /// <summary>
        /// Carries a failed bind from <see cref="PreStart"/> back into the mailbox.
        /// </summary>
        private sealed class BindFailed : INoSerializationVerificationNeeded
        {
            public BindFailed(ConductorBindException cause)
            {
                Cause = cause;
            }

            public ConductorBindException Cause { get; }
        }

        int _initialParticipants;
        readonly TestConductorSettings _settings = TestConductor.Get(Context.System).Settings;

        /// <summary>
        /// Set once the bind started in <see cref="PreStart"/> reports back. Non-null for the whole
        /// of the <see cref="Ready"/> behavior.
        /// </summary>
        private IChannel _connection;
        IActorRef _barrier;
        ImmutableDictionary<RoleName, NodeInfo> _nodes =
            ImmutableDictionary.Create<RoleName, NodeInfo>();
        // map keeping unanswered queries for node addresses (enqueued upon GetAddress, serviced upon NodeInfo)
        ImmutableDictionary<RoleName, ImmutableHashSet<IActorRef>> _addrInterest =
            ImmutableDictionary.Create<RoleName, ImmutableHashSet<IActorRef>>();
        int _generation = 1;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IPEndPoint _controllerPort;

        /// <summary>
        /// Null unless the caller asked to be told the bind outcome. See the constructor.
        /// </summary>
        private readonly TaskCompletionSource<IPEndPoint> _bindResult;

        public Controller(int initialParticipants, IPEndPoint controllerPort)
            : this(initialParticipants, controllerPort, null)
        {
        }

        /// <param name="initialParticipants">Number of clients that must attach before the run starts.</param>
        /// <param name="controllerPort">Endpoint to bind. Port 0 asks the OS for a free port.</param>
        /// <param name="bindResult">
        /// Optional. Receives the endpoint that was actually bound, or the bind failure. The caller
        /// needs both without an ask: an ask turns a failed bind into a query timeout, and every
        /// client node then burns its own attach timeout before the run gives up.
        /// </param>
        public Controller(int initialParticipants, IPEndPoint controllerPort, TaskCompletionSource<IPEndPoint> bindResult)
        {
            _initialParticipants = initialParticipants;
            _controllerPort = controllerPort;
            _bindResult = bindResult;
        }

        public IStash Stash { get; set; }

        /// <summary>
        /// Starts the bind. The result comes back as a <see cref="Bound"/> or
        /// <see cref="BindFailed"/> message, so the bind never blocks a dispatcher thread and
        /// its outcome is serialized with the rest of the mailbox.
        /// </summary>
        protected override void PreStart()
        {
            _log.Debug("Opening connection");
            RemoteConnection
                .CreateConnection(Role.Server, _controllerPort, _settings.ServerSocketWorkerPoolSize,
                    new ConductorHandler(Self, Logging.GetLogger(Context.System, typeof (ConductorHandler))))
                .PipeTo(Self,
                    success: channel => new Bound(channel),
                    // Name the failure here, so the message already carries the error the caller reports.
                    failure: ex => new BindFailed(ConductorBindException.ForEndpoint(_controllerPort, Unwrap(ex))));
        }

        /// <summary>
        /// Strips the single-exception AggregateException a piped task failure arrives in, so the
        /// reported cause is the socket error itself.
        /// </summary>
        private static Exception Unwrap(Exception cause)
        {
            if (cause is not AggregateException aggregate)
                return cause;

            var flattened = aggregate.Flatten();
            return flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
        }

        /// <summary>
        /// Initial behavior: waiting for the bind started in <see cref="PreStart"/> to report back.
        /// </summary>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Bound bound:
                    _connection = bound.Channel;
                    _log.Debug("Connection bound");

                    // Barrier first, report second. The caller must not see a bound conductor
                    // before this actor can serve barrier traffic.
                    _barrier = Context.ActorOf(Props.Create<BarrierCoordinator>(), "barriers");
                    _bindResult?.TrySetResult((IPEndPoint)_connection.LocalAddress);

                    Become(Ready);
                    Stash.UnstashAll();
                    return;

                case BindFailed failed:
                    _log.Error(failed.Cause, "Could not bind TestConductor controller");
                    _bindResult?.TrySetException(failed.Cause);

                    // Stop, never throw. A throw from a message handler runs the default
                    // supervision directive, Restart, which re-runs PreStart and binds the same
                    // dead port again - forever.
                    Context.Stop(Self);
                    return;

                default:
                    // The bound socket is the only source of real controller traffic, but it starts
                    // accepting the moment the bind completes - which is before this actor gets the
                    // Bound message. A client that connects in that window makes ConductorHandler
                    // send CreateServerFSM ahead of Bound. Hold anything that races in.
                    Stash.Stash();
                    return;
            }
        }

        /// <summary>
        /// Supervision of the BarrierCoordinator means to catch all his bad emotions
        /// and sometimes console him (BarrierEmpty, BarrierTimeout), sometimes tell
        /// him to hate the world (WrongBarrier, DuplicateNode, ClientLost). The latter shall help
        /// terminate broken tests as quickly as possible (i.e. without awaiting
        /// BarrierTimeouts in the players).
        /// </summary>
        /// <returns></returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(e =>
            {
                switch (e)
                {
                    case BarrierCoordinator.BarrierTimeoutException barrierTimeout:
                        return FailBarrier(barrierTimeout.BarrierData);
                    case BarrierCoordinator.FailedBarrierException failedBarrier:
                        return FailBarrier(failedBarrier.BarrierData);
                    case BarrierCoordinator.BarrierEmptyException barrierEmpty:
                        return Directive.Resume;
                    case BarrierCoordinator.WrongBarrierException wrongBarrier:
                        wrongBarrier.Client.Tell(new ToClient<BarrierResult>(new BarrierResult(wrongBarrier.Barrier, false)));
                        return FailBarrier(wrongBarrier.BarrierData);
                    case BarrierCoordinator.ClientLostException clientLost:
                        return FailBarrier(clientLost.BarrierData);
                    case BarrierCoordinator.DuplicateNodeException duplicateNode:
                        return FailBarrier(duplicateNode.BarrierData);
                    default: throw new InvalidOperationException($"Cannot process exception of type {e.GetType()}");
                }
            });
        }

        private Directive FailBarrier(BarrierCoordinator.Data data)
        {
            foreach(var c in data.Arrived) c.Tell(new ToClient<BarrierResult>(new BarrierResult(data.Barrier, false)));
            return Directive.Restart;
        }

        /// <summary>
        /// Behavior after a successful bind. <see cref="_connection"/> and <see cref="_barrier"/>
        /// are non-null here by construction of the state machine.
        /// </summary>
        private void Ready(object message)
        {
            var createServerFSM = message as CreateServerFSM;
            if (createServerFSM != null)
            {
                var channel = createServerFSM.Channel;
                var host = (IPEndPoint)channel.RemoteAddress;
                var name = WebUtility.UrlEncode(host + ":" + host.Port + "-server" + _generation++);
                var fsm = Context.ActorOf(
                    Props.Create(() => new ServerFSM(Self, channel)).WithDeploy(Deploy.Local), name);
                _log.Debug("Sending FSM {0} to {1}", fsm, Sender);
                Sender.Tell(fsm);
                return;
            }
            var nodeInfo = message as NodeInfo;
            if (nodeInfo != null)
            {
                _barrier.Forward(nodeInfo);
                if (_nodes.ContainsKey(nodeInfo.Name))
                {
                    if (_initialParticipants > 0)
                    {
                        foreach (var ni in _nodes.Values)
                            ni.FSM.Tell(new ToClient<BarrierResult>(new BarrierResult("initial startup", false)));
                        _initialParticipants = 0;
                    }
                    nodeInfo.FSM.Tell(new ToClient<BarrierResult>(new BarrierResult("initial startup", false)));
                }
                else
                {
                    _nodes = _nodes.Add(nodeInfo.Name, nodeInfo);
                    if(_initialParticipants <= 0) nodeInfo.FSM.Tell(new ToClient<Done>(Done.Instance));
                    else if (_nodes.Count == _initialParticipants)
                    {
                        foreach (var ni in _nodes.Values) ni.FSM.Tell(new ToClient<Done>(Done.Instance));
                        _initialParticipants = 0;
                    }

                    if (_addrInterest.TryGetValue(nodeInfo.Name, out var addr))
                    {
                        foreach(var a in addr)
                            a.Tell(new ToClient<AddressReply>(new AddressReply(nodeInfo.Name, nodeInfo.Addr)));
                        _addrInterest = _addrInterest.Remove(nodeInfo.Name);
                    }
                }
            }
            var clientDisconnected = message as ClientDisconnected;
            if (clientDisconnected != null && clientDisconnected.Name != null)
            {
                // A ServerFSM can report its disconnect after the same role has already come back
                // on a fresh channel, which is exactly what a node that calls StartNewSystem does.
                // Removing the role by name alone would drop the live registration, and from then
                // on the barrier coordinator ignores every arrival from that node, so the next
                // barrier stalls until it times out. Only let a ServerFSM evict the registration
                // it owns.
                if (_nodes.TryGetValue(clientDisconnected.Name, out var registered)
                    && !Equals(registered.FSM, Sender))
                {
                    _log.Debug("Ignoring disconnect of superseded connection for {0}", clientDisconnected.Name);
                    return;
                }

                _nodes = _nodes.Remove(clientDisconnected.Name);
                _barrier.Forward(clientDisconnected);
                return;
            }
            if (message is IServerOp)
            {
                switch (message)
                {
                    case EnterBarrier _: _barrier.Forward(message); return;
                    case FailBarrier _: _barrier.Forward(message); return;
                    case GetAddress getAddress:
                        var node = getAddress.Node;
                        if (_nodes.TryGetValue(node, out var replyNodeInfo))
                            Sender.Tell(new ToClient<AddressReply>(new AddressReply(node, replyNodeInfo.Addr)));
                        else
                        {
                            _addrInterest = _addrInterest.SetItem(node,
                                (_addrInterest.TryGetValue(node, out var existing)
                                    ? existing
                                    : ImmutableHashSet.Create<IActorRef>()
                                ).Add(Sender));
                        }
                        return;
                    case Done _: return; //FIXME what should happen?
                }
            }
            if (message is ICommandOp)
            {
                switch (message)
                {
                    case Throttle throttle:
                    {
                        if (!_nodes.TryGetValue(throttle.Target, out var target)) throw new IllegalActorStateException($"Throttle target {throttle.Target} was not found among nodes registered in {nameof(Controller)}: {string.Join(", ", _nodes.Keys)}");
                        if (!_nodes.TryGetValue(throttle.Node, out var source)) throw new IllegalActorStateException($"Throttle source {throttle.Node} was not found among nodes registered in {nameof(Controller)}: {string.Join(", ", _nodes.Keys)}");
                        
                        source.FSM.Forward(new ToClient<ThrottleMsg>(new ThrottleMsg(target.Addr, throttle.Direction, throttle.RateMBit)));
                        return;
                    }
                    case Disconnect disconnect:
                    {
                        if (!_nodes.TryGetValue(disconnect.Target, out var target)) throw new IllegalActorStateException($"Disconnect target {disconnect.Target} was not found among nodes registered in {nameof(Controller)}: {string.Join(", ", _nodes.Keys)}");
                        if (!_nodes.TryGetValue(disconnect.Node, out var source)) throw new IllegalActorStateException($"Disconnect source {disconnect.Node} was not found among nodes registered in {nameof(Controller)}: {string.Join(", ", _nodes.Keys)}");

                        source.FSM.Forward((new ToClient<DisconnectMsg>(new DisconnectMsg(target.Addr, disconnect.Abort))));
                        return;
                    }
                    case Terminate terminate:
                    {
                        _barrier.Tell(new BarrierCoordinator.RemoveClient(terminate.Node));

                        if (!_nodes.TryGetValue(terminate.Node, out var node)) throw new IllegalActorStateException($"Terminate target {terminate.Node} was not found among nodes registered in {nameof(Controller)}: {string.Join(", ", _nodes.Keys)}");

                        node.FSM.Forward(new ToClient<TerminateMsg>(new TerminateMsg(terminate.ShutdownOrExit)));
                        _nodes = _nodes.Remove(terminate.Node);
                        return;
                        }
                    case Remove remove:
                        _barrier.Tell(new BarrierCoordinator.RemoveClient(remove.Node));
                        return;
                }
            }
            if (message is GetNodes)
            {
                Sender.Tell(_nodes.Keys);
                return;
            }
            if (message is GetSockAddr)
            {
                Sender.Tell(_connection.LocalAddress);
                return;
            }
        }

        protected override void PostStop()
        {
            try
            {
                // Null when the bind failed: this actor stops without ever holding a connection.
                if (_connection is not null)
                    RemoteConnection.Shutdown(_connection);

                // PostStop cannot await, and blocking the dispatcher on a drain is what this class
                // is being cleaned of. ReleaseAll detaches the pools before it returns, so any
                // later CreateConnection builds fresh ones; only the graceful drain runs on.
                RemoteConnection.ReleaseAll().ContinueWith(
                    t => _log.Error(t.Exception, "Error while terminating RemoteConnection."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error while terminating RemoteConnection.");
            }
        }
    }
}

