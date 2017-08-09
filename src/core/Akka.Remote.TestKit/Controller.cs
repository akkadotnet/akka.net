﻿//-----------------------------------------------------------------------
// <copyright file="Controller.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal class Controller : UntypedActor, ILogReceive
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
                return obj is ClientDisconnected && Equals((ClientDisconnected) obj);
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

#if SERIALIZATION
            /// <summary>
            /// Initializes a new instance of the <see cref="ClientDisconnectedException"/> class.
            /// </summary>
            /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
            /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
            protected ClientDisconnectedException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
#endif
        }

        public class GetNodes
        {
            private GetNodes() { }
            private static readonly GetNodes _instance = new GetNodes();

            public static GetNodes Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        public class GetSockAddr
        {
            private GetSockAddr() { }
            private static readonly GetSockAddr _instance = new GetSockAddr();

            public static GetSockAddr Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Marker interface for working with <see cref="BarrierCoordinator"/>
        /// </summary>
        internal interface IHaveNodeInfo
        {
            NodeInfo Node { get; }
        }

        internal sealed class NodeInfo
        {
            readonly RoleName _name;
            readonly Address _addr;
            readonly IActorRef _fsm;

            public NodeInfo(RoleName name, Address addr, IActorRef fsm)
            {
                _name = name;
                _addr = addr;
                _fsm = fsm;
            }

            public RoleName Name
            {
                get { return _name; }
            }

            public Address Addr
            {
                get { return _addr; }
            }

            public IActorRef FSM
            {
                get { return _fsm; }
            }

            bool Equals(NodeInfo other)
            {
                return Equals(_name, other._name) && Equals(_addr, other._addr) && Equals(_fsm, other._fsm);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is NodeInfo && Equals((NodeInfo) obj);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (_name != null ? _name.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (_addr != null ? _addr.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (_fsm != null ? _fsm.GetHashCode() : 0);
                    return hashCode;
                }
            }

            /// <summary>
            /// Compares two specified <see cref="NodeInfo"/> for equality.
            /// </summary>
            /// <param name="left">The first <see cref="NodeInfo"/> used for comparison</param>
            /// <param name="right">The second <see cref="NodeInfo"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="NodeInfo"/> are equal; otherwise <c>false</c></returns>
            public static bool operator ==(NodeInfo left, NodeInfo right)
            {
                return Equals(left, right);
            }

            /// <summary>
            /// Compares two specified <see cref="NodeInfo"/> for inequality.
            /// </summary>
            /// <param name="left">The first <see cref="NodeInfo"/> used for comparison</param>
            /// <param name="right">The second <see cref="NodeInfo"/> used for comparison</param>
            /// <returns><c>true</c> if both <see cref="NodeInfo"/> are not equal; otherwise <c>false</c></returns>
            public static bool operator !=(NodeInfo left, NodeInfo right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class CreateServerFSM : INoSerializationVerificationNeeded
        {
            public CreateServerFSM(IChannel channel)
            {
                Channel = channel;
            }

            public IChannel Channel { get; private set; }
        }

        int _initialParticipants;
        readonly TestConductorSettings _settings = TestConductor.Get(Context.System).Settings;

        /// <summary>
        /// Lazily load the result later
        /// </summary>
        private IChannel _connection;
        readonly IActorRef _barrier;
        ImmutableDictionary<RoleName, NodeInfo> _nodes =
            ImmutableDictionary.Create<RoleName, NodeInfo>();
        // map keeping unanswered queries for node addresses (enqueued upon GetAddress, serviced upon NodeInfo)
        ImmutableDictionary<RoleName, ImmutableHashSet<IActorRef>> _addrInterest =
            ImmutableDictionary.Create<RoleName, ImmutableHashSet<IActorRef>>();
        int _generation = 1;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public Controller(int initialParticipants, IPEndPoint controllerPort)
        {
            _log.Debug("Opening connection");
            _connection = RemoteConnection.CreateConnection(Role.Server, controllerPort, _settings.ServerSocketWorkerPoolSize,
                new ConductorHandler(Self, Logging.GetLogger(Context.System, typeof (ConductorHandler)))).Result;
            _log.Debug("Connection bound");
            _barrier = Context.ActorOf(Props.Create<BarrierCoordinator>(), "barriers");
            _initialParticipants = initialParticipants;
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
                var barrierTimeout = e as BarrierCoordinator.BarrierTimeoutException;
                if (barrierTimeout != null) return FailBarrier(barrierTimeout.BarrierData);
                var failedBarrier = e as BarrierCoordinator.FailedBarrierException;
                if (failedBarrier != null) return FailBarrier(failedBarrier.BarrierData);
                var barrierEmpty = e as BarrierCoordinator.BarrierEmptyException;
                if(barrierEmpty != null) return Directive.Resume;
                var wrongBarrier = e as BarrierCoordinator.WrongBarrierException;
                if (wrongBarrier != null)
                {
                    wrongBarrier.Client.Tell(new ToClient<BarrierResult>(new BarrierResult(wrongBarrier.Barrier, false)));
                    return FailBarrier(wrongBarrier.BarrierData);
                }
                var clientLost = e as BarrierCoordinator.ClientLostException;
                if (clientLost != null) return FailBarrier(clientLost.BarrierData);
                var duplicateNode = e as BarrierCoordinator.DuplicateNodeException;
                if (duplicateNode != null) return FailBarrier(duplicateNode.BarrierData);
                throw new InvalidOperationException($"Cannot process exception of type {e.GetType()}");
            });
        }

        private Directive FailBarrier(BarrierCoordinator.Data data)
        {
            foreach(var c in data.Arrived) c.Tell(new ToClient<BarrierResult>(new BarrierResult(data.Barrier, false)));
            return Directive.Restart;
        }

        protected override void OnReceive(object message)
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
                _nodes = _nodes.Remove(clientDisconnected.Name);
                _barrier.Forward(clientDisconnected);
                return;
            }
            if (message is IServerOp)
            {
                if (message is EnterBarrier)
                {
                    _barrier.Forward(message);
                    return;
                }
                if (message is FailBarrier)
                {
                    _barrier.Forward(message);
                    return;                    
                }
                var getAddress = message as GetAddress;
                if (getAddress != null)
                {
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
                }
                if (message is Done) return; //FIXME what should happen?
            }
            if (message is ICommandOp)
            {
                var throttle = message as Throttle;
                if (throttle != null)
                {
                    var t = _nodes[throttle.Target];
                   _nodes[throttle.Node].FSM.Forward(new ToClient<ThrottleMsg>(new ThrottleMsg(t.Addr, throttle.Direction, throttle.RateMBit)));
                    return;
                }
                var disconnect = message as Disconnect;
                if (disconnect != null)
                {
                    var t = _nodes[disconnect.Target];
                    _nodes[disconnect.Node].FSM.Forward((new ToClient<DisconnectMsg>(new DisconnectMsg(t.Addr, disconnect.Abort))));
                    return;
                }
                var terminate = message as Terminate;
                if (terminate != null)
                {
                    _barrier.Tell(new BarrierCoordinator.RemoveClient(terminate.Node));
                    _nodes[terminate.Node].FSM.Forward(new ToClient<TerminateMsg>(new TerminateMsg(terminate.ShutdownOrExit)));
                    _nodes = _nodes.Remove(terminate.Node);
                    return;
                }
                var remove = message as Remove;
                if (remove != null)
                {
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
                RemoteConnection.Shutdown(_connection);
                RemoteConnection.ReleaseAll().Wait(_settings.ConnectTimeout);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error while terminating RemoteConnection.");
            }
        }
    }
}

