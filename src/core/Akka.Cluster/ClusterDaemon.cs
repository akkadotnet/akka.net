//-----------------------------------------------------------------------
// <copyright file="ClusterDaemon.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// Base interface for all cluster messages. All ClusterMessage's are serializable.
    /// </summary>
    public interface IClusterMessage
    {
    }

    /// <summary>
    /// Cluster commands sent by the USER via <see cref="Cluster"/> extension.
    /// </summary>
    internal class ClusterUserAction
    {
        internal abstract class BaseClusterUserAction
        {
            readonly Address _address;

            public Address Address { get { return _address; } }

            protected BaseClusterUserAction(Address address)
            {
                _address = address;
            }

            public override bool Equals(object obj)
            {
                var baseUserAction = (BaseClusterUserAction)obj;
                return baseUserAction != null && Equals(baseUserAction);
            }

            protected bool Equals(BaseClusterUserAction other)
            {
                return Equals(_address, other._address);
            }

            public override int GetHashCode()
            {
                return (_address != null ? _address.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Command to initiate join another node (represented by `address`).
        /// Join will be sent to the other node.
        /// </summary>
        internal sealed class JoinTo : BaseClusterUserAction
        {
            public JoinTo(Address address)
                : base(address)
            {
            }
        }

        /// <summary>
        /// Command to leave the cluster.
        /// </summary>
        internal sealed class Leave : BaseClusterUserAction, IClusterMessage
        {
            public Leave(Address address)
                : base(address)
            { }
        }

        /// <summary>
        /// Command to mark node as temporary down.
        /// </summary>
        internal sealed class Down : BaseClusterUserAction, IClusterMessage
        {
            public Down(Address address)
                : base(address)
            {
            }
        }
    }

    /// <summary>
    /// Command to join the cluster. Sent when a node wants to join another node (the receiver).
    /// </summary>
    internal sealed class InternalClusterAction
    {
        internal sealed class Join : IClusterMessage
        {
            readonly UniqueAddress _node;
            readonly ImmutableHashSet<string> _roles;

            /// <param name="node">the node that wants to join the cluster</param>
            /// <param name="roles"></param>
            public Join(UniqueAddress node, ImmutableHashSet<string> roles)
            {
                _node = node;
                _roles = roles;
            }

            public UniqueAddress Node { get { return _node; } }
            public ImmutableHashSet<string> Roles { get { return _roles; } }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Join && Equals((Join)obj);
            }

            private bool Equals(Join other)
            {
                return _node.Equals(other._node) && !_roles.Except(other._roles).Any();
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_node.GetHashCode() * 397) ^ _roles.GetHashCode();
                }
            }

            public override string ToString()
            {
                return string.Format("{0}: {1} wants to join on Roles [{2}]", GetType(), Node,
                    string.Join(",", Roles ?? ImmutableHashSet<string>.Empty));
            }
        }

        /// <summary>
        /// Reply to Join
        /// </summary>
        internal sealed class Welcome : IClusterMessage
        {
            readonly UniqueAddress _from;
            readonly Gossip _gossip;

            /// <param name="from">the sender node in the cluster, i.e. the node that received the Join command</param>
            /// <param name="gossip"></param>
            public Welcome(UniqueAddress from, Gossip gossip)
            {
                _from = from;
                _gossip = gossip;
            }

            public UniqueAddress From { get { return _from; } }

            public Gossip Gossip { get { return _gossip; } }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Welcome && Equals((Welcome)obj);
            }

            private bool Equals(Welcome other)
            {
                return _from.Equals(other._from) && _gossip.ToString().Equals(other._gossip.ToString());
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_from.GetHashCode() * 397) ^ _gossip.GetHashCode();
                }
            }

        }

        /// <summary>
        /// Command to initiate the process to join the specified
        /// seed nodes.
        /// </summary>
        internal sealed class JoinSeedNodes
        {
            readonly ImmutableList<Address> _seedNodes;

            public JoinSeedNodes(ImmutableList<Address> seedNodes)
            {
                _seedNodes = seedNodes;
            }

            public ImmutableList<Address> SeedNodes
            {
                get { return _seedNodes; }
            }
        }

        /// <summary>
        /// Start message of the process to join one of the seed nodes.
        /// The node sends <see cref="InitJoin"/> to all seed nodes, which replies
        /// with <see cref="InitJoinAck"/>. The first reply is used others are discarded.
        /// The node sends <see cref="Join"/> command to the seed node that replied first.
        /// If a node is uninitialized it will reply to `InitJoin` with
        /// <see cref="InitJoinNack"/>.
        /// </summary>
        internal class JoinSeenNode
        {
        }

        /// <summary>
        /// See JoinSeedNode
        /// </summary>
        internal class InitJoin : IClusterMessage
        {
            public override bool Equals(object obj)
            {
                return obj is InitJoin;
            }
        }

        /// <summary>
        /// See JoinSeeNode
        /// </summary>
        internal sealed class InitJoinAck : IClusterMessage
        {
            readonly Address _address;

            public InitJoinAck(Address address)
            {
                _address = address;
            }

            public Address Address
            {
                get { return _address; }
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is InitJoinAck && Equals((InitJoinAck)obj);
            }

            private bool Equals(InitJoinAck other)
            {
                return Equals(_address, other._address);
            }

            public override int GetHashCode()
            {
                return (_address != null ? _address.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// See JoinSeeNode
        /// </summary>
        internal sealed class InitJoinNack : IClusterMessage
        {
            readonly Address _address;

            public InitJoinNack(Address address)
            {
                _address = address;
            }

            public Address Address
            {
                get { return _address; }
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is InitJoinNack && Equals((InitJoinNack)obj);
            }

            private bool Equals(InitJoinNack other)
            {
                return Equals(_address, other._address);
            }

            public override int GetHashCode()
            {
                return (_address != null ? _address.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Marker interface for periodic tick messages
        /// </summary>
        internal interface ITick { }

        internal class GossipTick : ITick
        {
            private GossipTick() { }
            private static readonly GossipTick _instance = new GossipTick();
            public static GossipTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal class GossipSpeedupTick : ITick
        {
            private GossipSpeedupTick() { }
            private static readonly GossipSpeedupTick _instance = new GossipSpeedupTick();
            public static GossipSpeedupTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal class ReapUnreachableTick : ITick
        {
            private ReapUnreachableTick() { }
            private static readonly ReapUnreachableTick _instance = new ReapUnreachableTick();
            public static ReapUnreachableTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal class MetricsTick : ITick
        {
            private MetricsTick() { }
            private static readonly MetricsTick _instance = new MetricsTick();
            public static MetricsTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal class LeaderActionsTick : ITick
        {
            private LeaderActionsTick() { }
            private static readonly LeaderActionsTick _instance = new LeaderActionsTick();
            public static LeaderActionsTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal class PublishStatsTick : ITick
        {
            private PublishStatsTick() { }
            private static readonly PublishStatsTick _instance = new PublishStatsTick();
            public static PublishStatsTick Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        internal sealed class SendGossipTo
        {
            readonly Address _address;

            public SendGossipTo(Address address)
            {
                _address = address;
            }

            public Address Address { get { return _address; } }

            public override bool Equals(object obj)
            {
                var other = obj as SendGossipTo;
                if (other == null) return false;
                return _address.Equals(other._address);
            }

            public override int GetHashCode()
            {
                return _address.GetHashCode();
            }
        }

        internal class GetClusterCoreRef
        {
            private GetClusterCoreRef() { }
            private static readonly GetClusterCoreRef _instance = new GetClusterCoreRef();
            public static GetClusterCoreRef Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Command to <see cref="Akka.Cluster.ClusterDaemon"/> to create a
        /// <see cref="OnMemberStatusChangedListener"/>
        /// </summary>
        public sealed class AddOnMemberUpListener : INoSerializationVerificationNeeded
        {
            public AddOnMemberUpListener(Action callback)
            {
                Callback = callback;
            }

            public Action Callback { get; }
        }

        /// <summary>
        /// Command to the <see cref="ClusterDaemon"/> to create a 
        /// </summary>
        public sealed class AddOnMemberRemovedListener : INoSerializationVerificationNeeded
        {
            public AddOnMemberRemovedListener(Action callback)
            {
                Callback = callback;
            }

            public Action Callback { get; }
        }

        public interface ISubscriptionMessage { }

        public sealed class Subscribe : ISubscriptionMessage
        {
            readonly IActorRef _subscriber;
            readonly ClusterEvent.SubscriptionInitialStateMode _initialStateMode;
            readonly ImmutableHashSet<Type> _to;

            public Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode,
                ImmutableHashSet<Type> to)
            {
                _subscriber = subscriber;
                _initialStateMode = initialStateMode;
                _to = to;
            }

            public IActorRef Subscriber
            {
                get { return _subscriber; }
            }

            public ClusterEvent.SubscriptionInitialStateMode InitialStateMode
            {
                get { return _initialStateMode; }
            }

            public ImmutableHashSet<Type> To
            {
                get { return _to; }
            }
        }

        public sealed class Unsubscribe : ISubscriptionMessage
        {
            readonly IActorRef _subscriber;
            readonly Type _to;

            public Unsubscribe(IActorRef subscriber, Type to)
            {
                _to = to;
                _subscriber = subscriber;
            }

            public IActorRef Subscriber
            {
                get { return _subscriber; }
            }

            public Type To
            {
                get { return _to; }
            }
        }

        public sealed class SendCurrentClusterState : ISubscriptionMessage
        {
            readonly IActorRef _receiver;

            public IActorRef Receiver
            {
                get { return _receiver; }
            }

            /// <param name="receiver"><see cref="Akka.Cluster.ClusterEvent.CurrentClusterState"/> will be sent to the `receiver`</param>
            public SendCurrentClusterState(IActorRef receiver)
            {
                _receiver = receiver;
            }
        }

        interface IPublishMessage { }

        internal sealed class PublishChanges : IPublishMessage
        {
            readonly Gossip _newGossip;

            internal PublishChanges(Gossip newGossip)
            {
                _newGossip = newGossip;
            }

            public Gossip NewGossip
            {
                get { return _newGossip; }
            }
        }

        internal sealed class PublishEvent : IPublishMessage
        {
            readonly ClusterEvent.IClusterDomainEvent _event;

            internal PublishEvent(ClusterEvent.IClusterDomainEvent @event)
            {
                _event = @event;
            }

            public ClusterEvent.IClusterDomainEvent Event
            {
                get { return _event; }
            }
        }
    }

    /// <summary>
    /// Supervisor managing the different Cluster daemons.
    /// </summary>
    internal sealed class ClusterDaemon : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private IActorRef _coreSupervisor;
        private readonly ClusterSettings _settings;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ClusterDaemon(ClusterSettings settings)
        {
            // Important - don't use Cluster(context.system) in constructor because that would
            // cause deadlock. The Cluster extension is currently being created and is waiting
            // for response from GetClusterCoreRef in its constructor.
            // Child actors are therefore created when GetClusterCoreRef is received
            _coreSupervisor = null;
            _settings = settings;

            Receive<InternalClusterAction.GetClusterCoreRef>(msg =>
            {
                if(_coreSupervisor == null)
                    CreateChildren();
                _coreSupervisor.Forward(msg);
            });

            Receive<InternalClusterAction.AddOnMemberUpListener>(msg =>
            {
                Context.ActorOf(
                    Props.Create(() => new OnMemberStatusChangedListener(msg.Callback, MemberStatus.Up))
                        .WithDeploy(Deploy.Local));
            });

            Receive<InternalClusterAction.AddOnMemberRemovedListener>(msg =>
            {
                Context.ActorOf(
                    Props.Create(() => new OnMemberStatusChangedListener(msg.Callback, MemberStatus.Removed))
                        .WithDeploy(Deploy.Local));
            });
        }

        private void CreateChildren()
        {
            _coreSupervisor = Context.ActorOf(Props.Create<ClusterCoreSupervisor>(), "core");

            Context.ActorOf(Props.Create<ClusterHeartbeatReceiver>(), "heartbeatReceiver");
        }
    }

    /// <summary>
    /// ClusterCoreDaemon and ClusterDomainEventPublisher can't be restarted because the state
    /// would be obsolete. Shutdown the member if any those actors crashed.
    /// </summary>
    internal class ClusterCoreSupervisor : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private IActorRef _publisher;
        private IActorRef _coreDaemon;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ClusterCoreSupervisor()
        {
            // Important - don't use Cluster(Context.System) in constructor because that would
            // cause deadlock. The Cluster extension is currently being created and is waiting
            // for response from GetClusterCoreRef in its constructor.
            // Child actors are therefore created when GetClusterCoreRef is received

            Receive<InternalClusterAction.GetClusterCoreRef>(cr =>
            {
                if(_coreDaemon == null)
                    CreateChildren();
                Sender.Tell(_coreDaemon);
            });
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(e =>
            {
                //TODO: JVM version matches NonFatal. Can / should we do something similar? 
                _log.Error(e, "Cluster node [{0}] crashed, [{1}] - shutting down...",
                    Cluster.Get(Context.System).SelfAddress, e);
                Self.Tell(PoisonPill.Instance);
                return Directive.Stop;
            });
        }

        protected override void PostStop()
        {
            Cluster.Get(Context.System).Shutdown();
        }

        private void CreateChildren()
        {
            _publisher =
                Context.ActorOf(Props.Create<ClusterDomainEventPublisher>().WithDispatcher(Context.Props.Dispatcher), "publisher");
            _coreDaemon = Context.ActorOf(Props.Create(() => new ClusterCoreDaemon(_publisher)).WithDispatcher(Context.Props.Dispatcher), "daemon");
            Context.Watch(_coreDaemon);
        }
    }

    internal class ClusterCoreDaemon : UntypedActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly Cluster _cluster;
        protected readonly UniqueAddress SelfUniqueAddress;
        private const int NumberOfGossipsBeforeShutdownWhenLeaderExits = 3;
        private const int MaxGossipsBeforeShuttingDownMyself = 5;

        private readonly VectorClock.Node _vclockNode;

        private string VclockName(UniqueAddress node)
        {
            return node.Address + "-" + node.Uid;
        }

        // note that self is not initially member,
        // and the SendGossip is not versioned for this 'Node' yet
        Gossip _latestGossip = Gossip.Empty;

        readonly bool _statsEnabled;
        private GossipStats _gossipStats = new GossipStats();
        private ImmutableList<Address> _seedNodes;
        private IActorRef _seedNodeProcess;
        private int _seedNodeProcessCounter = 0; //for unique names
        private bool _logInfo;

        readonly IActorRef _publisher;
        private int _leaderActionCounter = 0;

        public ClusterCoreDaemon(IActorRef publisher)
        {
            _cluster = Cluster.Get(Context.System);
            _publisher = publisher;
            SelfUniqueAddress = _cluster.SelfUniqueAddress;
            _vclockNode = new VectorClock.Node(VclockName(SelfUniqueAddress));
            var settings = _cluster.Settings;
            var scheduler = _cluster.Scheduler;
            _seedNodes = _cluster.Settings.SeedNodes;

            _statsEnabled = settings.PublishStatsInterval.HasValue
                            && settings.PublishStatsInterval >= TimeSpan.Zero
                            && settings.PublishStatsInterval != TimeSpan.MaxValue;

            // start periodic gossip to random nodes in cluster
            _gossipTaskCancellable =
                scheduler.ScheduleTellRepeatedlyCancelable(
                    settings.PeriodicTasksInitialDelay.Max(settings.GossipInterval), 
                    settings.GossipInterval, 
                    Self, 
                    InternalClusterAction.GossipTick.Instance, 
                    Self);

            // start periodic cluster failure detector reaping (moving nodes condemned by the failure detector to unreachable list)
            _failureDetectorReaperTaskCancellable =
                scheduler.ScheduleTellRepeatedlyCancelable(
                    settings.PeriodicTasksInitialDelay.Max(settings.UnreachableNodesReaperInterval), 
                    settings.UnreachableNodesReaperInterval, 
                    Self, 
                    InternalClusterAction.ReapUnreachableTick.Instance, 
                    Self);

            // start periodic leader action management (only applies for the current leader)
            _leaderActionsTaskCancellable =
                scheduler.ScheduleTellRepeatedlyCancelable(
                    settings.PeriodicTasksInitialDelay.Max(settings.LeaderActionsInterval), 
                    settings.LeaderActionsInterval, 
                    Self, 
                    InternalClusterAction.LeaderActionsTick.Instance, 
                    Self);

            // start periodic publish of current stats
            if (settings.PublishStatsInterval != null && settings.PublishStatsInterval > TimeSpan.Zero && settings.PublishStatsInterval != TimeSpan.MaxValue)
            {
                _publishStatsTaskTaskCancellable =
                    scheduler.ScheduleTellRepeatedlyCancelable(
                        settings.PeriodicTasksInitialDelay.Max(settings.PublishStatsInterval.Value), 
                        settings.PublishStatsInterval.Value, 
                        Self, 
                        InternalClusterAction.PublishStatsTick.Instance, 
                        Self);
            }

            _logInfo = settings.LogInfo;
        }

        ActorSelection ClusterCore(Address address)
        {
            return Context.ActorSelection(new RootActorPath(address) / "system" / "cluster" / "core" / "daemon");
        }

        readonly ICancelable _gossipTaskCancellable;
        readonly ICancelable _failureDetectorReaperTaskCancellable;
        readonly ICancelable _leaderActionsTaskCancellable;
        readonly ICancelable _publishStatsTaskTaskCancellable;

        protected override void PreStart()
        {
            Context.System.EventStream.Subscribe(Self, typeof(QuarantinedEvent));

            // TODO: replace to DowningProvider
            if (_cluster.Settings.AutoDownUnreachableAfter != null)
                Context.ActorOf(
                    AutoDown.Props(_cluster.Settings.AutoDownUnreachableAfter.Value),
                    "autoDown");

            if (_seedNodes.IsEmpty)
            {
                _log.Info("No seed-nodes configured, manual cluster join required");
            }
            else
            {
                Self.Tell(new InternalClusterAction.JoinSeedNodes(_seedNodes));
            }
        }

        protected override void PostStop()
        {
            Context.System.EventStream.Unsubscribe(Self);
            _gossipTaskCancellable.Cancel();
            _failureDetectorReaperTaskCancellable.Cancel();
            _leaderActionsTaskCancellable.Cancel();
            if (_publishStatsTaskTaskCancellable != null) _publishStatsTaskTaskCancellable.Cancel();
        }

        private void Uninitialized(object message)
        {
            if (_logInfo && !(message is InternalClusterAction.ITick))
            {
                _log.Debug("[Uninitialized] Received {0}", message);
            }

            if (message is InternalClusterAction.InitJoin)
            {
                Sender.Tell(new InternalClusterAction.InitJoinNack(_cluster.SelfAddress));
            }
            else if (message is ClusterUserAction.JoinTo)
            {
                var jt = message as ClusterUserAction.JoinTo;
                Join(jt.Address);
            }
            else if (message is InternalClusterAction.JoinSeedNodes)
            {
                var js = message as InternalClusterAction.JoinSeedNodes;
                JoinSeedNodes(js.SeedNodes);
            }
            else if (message is InternalClusterAction.ISubscriptionMessage)
            {
                var isub = message as InternalClusterAction.ISubscriptionMessage;
                _publisher.Forward(isub);
            }
            else
            {
                Unhandled(message);
            }
        }

        private void TryingToJoin(object message, Address joinWith, Deadline deadline)
        {
            if (_logInfo && !(message is InternalClusterAction.ITick))
            {
                _log.Debug("[TryingToJoin] Received {0}", message);
            }

            if (message is InternalClusterAction.Welcome)
            {
                var w = message as InternalClusterAction.Welcome;
                Welcome(joinWith, w.From, w.Gossip);
            }
            else if (message is InternalClusterAction.InitJoin)
            {
                Sender.Tell(new InternalClusterAction.InitJoinNack(_cluster.SelfAddress));
            }
            else if (message is ClusterUserAction.JoinTo)
            {
                var jt = message as ClusterUserAction.JoinTo;
                BecomeUninitialized();
                Join(jt.Address);
            }
            else if (message is InternalClusterAction.JoinSeedNodes)
            {
                var js = message as InternalClusterAction.JoinSeedNodes;
                JoinSeedNodes(js.SeedNodes);
            }
            else if (message is InternalClusterAction.ISubscriptionMessage)
            {
                var isub = message as InternalClusterAction.ISubscriptionMessage;
                _publisher.Forward(isub);
            }
            else if (message is InternalClusterAction.ITick)
            {
                if (deadline != null && deadline.IsOverdue)
                {
                    // join attempt failed, retry
                    BecomeUninitialized();
                    if (!_seedNodes.IsEmpty) JoinSeedNodes(_seedNodes);
                    else Join(joinWith);
                }
            }
            else
            {
                Unhandled(message);
            }
        }

        private void BecomeUninitialized()
        {
            // make sure that join process is stopped
            StopSeedNodeProcess();
            Context.Become(Uninitialized);
        }

        private void BecomeInitialized()
        {
            // start heartbeatSender here, and not in constructor to make sure that
            // heartbeating doesn't start before Welcome is received            
            Context.ActorOf(Props.Create<ClusterHeartbeatSender>().WithDispatcher(_cluster.Settings.UseDispatcher),
                "heartbeatSender");
            // make sure that join process is stopped
            StopSeedNodeProcess();
            Context.Become(Initialized);
        }

        private void Initialized(object message)
        {
            if (_logInfo && !(message is InternalClusterAction.ITick))
            {
                _log.Debug("[Initialized] Received {0}", message);
            }

            if (message is GossipEnvelope)
            {
                var ge = message as GossipEnvelope;
                ReceiveGossip(ge);
            }
            else if (message is GossipStatus)
            {
                var gs = message as GossipStatus;
                ReceiveGossipStatus(gs);
            }
            else if (message is InternalClusterAction.GossipTick)
            {
                GossipTick();
            }
            else if (message is InternalClusterAction.GossipSpeedupTick)
            {
                GossipSpeedupTick();
            }
            else if (message is InternalClusterAction.ReapUnreachableTick)
            {
                ReapUnreachableMembers();
            }
            else if (message is InternalClusterAction.LeaderActionsTick)
            {
                LeaderActions();
            }
            else if (message is InternalClusterAction.PublishStatsTick)
            {
                PublishInternalStats();
            }
            else if (message is InternalClusterAction.InitJoin)
            {
                InitJoin();
            }
            else if (message is InternalClusterAction.Join)
            {
                var join = message as InternalClusterAction.Join;
                Joining(join.Node, join.Roles);
            }
            else if (message is ClusterUserAction.Down)
            {
                var down = message as ClusterUserAction.Down;
                Downing(down.Address);
            }
            else if (message is ClusterUserAction.Leave)
            {
                var leave = message as ClusterUserAction.Leave;
                Leaving(leave.Address);
            }
            else if (message is InternalClusterAction.SendGossipTo)
            {
                var sendGossipTo = message as InternalClusterAction.SendGossipTo;
                SendGossipTo(sendGossipTo.Address);
            }
            else if (message is InternalClusterAction.ISubscriptionMessage)
            {
                _publisher.Forward(message);
            }
            else if (message is QuarantinedEvent)
            {
                var q = message as QuarantinedEvent;
                Quarantined(new UniqueAddress(q.Address, q.Uid));
            }
            else if (message is ClusterUserAction.JoinTo)
            {
                var jt = message as ClusterUserAction.JoinTo;
                _log.Info("Trying to join [{0}] when already part of a cluster, ignoring", jt.Address);
            }
            else if (message is InternalClusterAction.JoinSeedNodes)
            {
                var joinSeedNodes = message as InternalClusterAction.JoinSeedNodes;
                _log.Info("Trying to join seed nodes [{0}] when already part of a cluster, ignoring",
                    joinSeedNodes.SeedNodes.Select(a => a.ToString()).Aggregate((a, b) => a + ", " + b));
            }
            else
            {
                Unhandled(message);
            }
        }

        protected override void OnReceive(object message)
        {
            Uninitialized(message);
        }

        protected override void Unhandled(object message)
        {
            if (message is InternalClusterAction.ITick || message is GossipEnvelope || message is GossipStatus)
            {
                //do nothing
            }
            else
            {
                if (_logInfo)
                {
                    _log.Debug("[Unhandled] Received {0}", message);
                }
                base.Unhandled(message);
            }
        }

        public void InitJoin()
        {
            var selfStatus = _latestGossip.GetMember(SelfUniqueAddress).Status;
            if (Gossip.RemoveUnreachableWithMemberStatus.Contains(selfStatus))
            {
                // prevents a Down and Exiting node from being used for joining
                Sender.Tell(new InternalClusterAction.InitJoinNack(_cluster.SelfAddress));
            }
            else
            {
                Sender.Tell(new InternalClusterAction.InitJoinAck(_cluster.SelfAddress));
            }
        }

        public void JoinSeedNodes(ImmutableList<Address> newSeedNodes)
        {
            if (!newSeedNodes.IsEmpty)
            {
                StopSeedNodeProcess();
                _seedNodes = newSeedNodes; // keep them for retry
                if (newSeedNodes.SequenceEqual(ImmutableList.Create(_cluster.SelfAddress)))
                {
                    Self.Tell(new ClusterUserAction.JoinTo(_cluster.SelfAddress));
                    _seedNodeProcess = null;
                }
                else
                {
                    // use unique name of this actor, stopSeedNodeProcess doesn't wait for termination
                    _seedNodeProcessCounter += 1;
                    if (newSeedNodes.Head().Equals(_cluster.SelfAddress))
                    {
                        _seedNodeProcess = Context.ActorOf(Props.Create(() => new FirstSeedNodeProcess(newSeedNodes)).WithDispatcher(_cluster.Settings.UseDispatcher), "firstSeedNodeProcess-" + _seedNodeProcessCounter);
                    }
                    else
                    {
                        _seedNodeProcess = Context.ActorOf(Props.Create(() => new JoinSeedNodeProcess(newSeedNodes)).WithDispatcher(_cluster.Settings.UseDispatcher), "joinSeedNodeProcess-" + _seedNodeProcessCounter);
                    }
                }
            }
        }

        /// <summary>
        /// Try to join this cluster node with the node specified by `address`.
        /// It's only allowed to join from an empty state, i.e. when not already a member.
        /// A `Join(selfUniqueAddress)` command is sent to the node to join,
        /// which will reply with a `Welcome` message.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Join can only be done from an empty state</exception>
        public void Join(Address address)
        {
            if (address.Protocol != _cluster.SelfAddress.Protocol)
            {
                _log.Warning("Trying to join member with wrong protocol, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.Protocol, address.Protocol);
            }
            else if (address.System != _cluster.SelfAddress.System)
            {
                _log.Warning("Trying to join member with wrong ActorSystem name, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.System, address.System);
            }
            else
            {
                //TODO: Akka exception?
                if (!_latestGossip.Members.IsEmpty) throw new InvalidOperationException("Join can only be done from an empty state");

                // to support manual join when joining to seed nodes is stuck (no seed nodes available)
                StopSeedNodeProcess();

                if (address.Equals(_cluster.SelfAddress))
                {
                    BecomeInitialized();
                    Joining(SelfUniqueAddress, _cluster.SelfRoles);
                }
                else
                {
                    var joinDeadline = _cluster.Settings.RetryUnsuccessfulJoinAfter == null
                        ? null
                        : Deadline.Now + _cluster.Settings.RetryUnsuccessfulJoinAfter;

                    Context.Become(m => TryingToJoin(m, address, joinDeadline));
                    ClusterCore(address).Tell(new InternalClusterAction.Join(_cluster.SelfUniqueAddress, _cluster.SelfRoles));
                }
            }
        }

        public void StopSeedNodeProcess()
        {
            if (_seedNodeProcess != null)
            {
                // manual join, abort current seedNodeProcess
                Context.Stop(_seedNodeProcess);
                _seedNodeProcess = null;
            }
            else
            {
                // no seedNodeProcess in progress
            }
        }


        /// <summary>
        /// State transition to JOINING - new node joining.
        /// Received `Join` message and replies with `Welcome` message, containing
        /// current gossip state, including the new joining member.
        /// </summary>
        public void Joining(UniqueAddress node, ImmutableHashSet<string> roles)
        {
            var selfStatus = _latestGossip.GetMember(SelfUniqueAddress).Status;
            if (!node.Address.Protocol.Equals(_cluster.SelfAddress.Protocol))
            {
                _log.Warning("Member with wrong protocol tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.Protocol, node.Address.Protocol);
            }
            else if (!node.Address.System.Equals(_cluster.SelfAddress.System))
            {
                _log.Warning("Member with wrong ActorSystem name tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.System, node.Address.System);
            }
            else if (Gossip.RemoveUnreachableWithMemberStatus.Contains(selfStatus))
            {
                _log.Info("Trying to join [{0}] to [{1}] member, ignoring. Use a member that is Up instead.", node, selfStatus);
            }
            else
            {
                var localMembers = _latestGossip.Members;

                // check by address without uid to make sure that node with same host:port is not allowed
                // to join until previous node with that host:port has been removed from the cluster
                var localMember = localMembers.FirstOrDefault(m => m.Address.Equals(node.Address));
                if (localMember != null && localMember.UniqueAddress.Equals(node))
                {
                    // node retried join attempt, probably due to lost Welcome message
                    _log.Info("Existing member [{0}] is joining again.", node);
                    if (!node.Equals(SelfUniqueAddress))
                    {
                        Sender.Tell(new InternalClusterAction.Welcome(SelfUniqueAddress, _latestGossip));
                    }
                }
                else if (localMember != null)
                {
                    // node restarted, same host:port as existing member, but with different uid
                    // safe to down and later remove existing member
                    // new node will retry join
                    _log.Info("New incarnation of existing member [{0}] is trying to join. " +
                        "Existing will be removed from the cluster and then new member will be allowed to join.", node);
                    if (localMember.Status != MemberStatus.Down)
                        Downing(localMember.Address);
                }
                else
                {
                    // remove the node from the failure detector
                    _cluster.FailureDetector.Remove(node.Address);

                    // add joining node as Joining
                    // add self in case someone else joins before self has joined (Set discards duplicates)
                    var newMembers = localMembers
                            .Add(Member.Create(node, roles))
                            .Add(Member.Create(_cluster.SelfUniqueAddress, _cluster.SelfRoles));
                    var newGossip = _latestGossip.Copy(members: newMembers);

                    UpdateLatestGossip(newGossip);

                    _log.Info("Node [{0}] is JOINING, roles [{1}]", node.Address, string.Join(",", roles));

                    if (node.Equals(SelfUniqueAddress))
                    {
                        if (localMembers.IsEmpty)
                        {
                            // important for deterministic oldest when bootstrapping
                            LeaderActions();
                        }
                    }
                    else 
                    {
                        Sender.Tell(new InternalClusterAction.Welcome(SelfUniqueAddress, _latestGossip));
                    }

                    Publish(_latestGossip);
                }
            }
        }

        /// <summary>
        /// Reply from Join request
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Welcome can only be done from an empty state</exception>
        public void Welcome(Address joinWith, UniqueAddress from, Gossip gossip)
        {
            if (!_latestGossip.Members.IsEmpty) throw new InvalidOperationException("Welcome can only be done from an empty state");
            if (!joinWith.Equals(from.Address))
            {
                _log.Info("Ignoring welcome from [{0}] when trying to join with [{1}]", from.Address, joinWith);
            }
            else
            {
                _log.Info("Welcome from [{0}]", from.Address);
                _latestGossip = gossip.Seen(SelfUniqueAddress);
                AssertLatestGossip();
                Publish(_latestGossip);
                if (!from.Equals(SelfUniqueAddress))
                    GossipTo(from, Sender);
                BecomeInitialized();
            }
        }

        /// <summary>
        /// State transition to LEAVING.
        /// The node will eventually be removed by the leader, after hand-off in EXITING, and only after
        /// removal a new node with same address can join the cluster through the normal joining procedure.
        /// </summary>
        /// <param name="address">The address.</param>
        public void Leaving(Address address)
        {
            // only try to update if the node is available (in the member ring)
            if (_latestGossip.Members.Any(m => m.Address.Equals(address) && m.Status == MemberStatus.Up))
            {
                // mark node as LEAVING
                var newMembers = _latestGossip.Members.Select(m =>
                {
                    if (m.Address == address) return m.Copy(status: MemberStatus.Leaving);
                    return m;
                }).ToImmutableSortedSet(); // mark node as LEAVING
                var newGossip = _latestGossip.Copy(members: newMembers);

                UpdateLatestGossip(newGossip);

                _log.Info("Marked address [{0}] as [{1}]", address, MemberStatus.Leaving);
                Publish(_latestGossip);
            }
        }

        /// <summary>
        /// This method is called when a member sees itself as Exiting or Down.
        /// </summary>
        public void Shutdown()
        {
            _cluster.Shutdown();
        }

        /// <summary>
        /// State transition to DOWN.
        /// Its status is set to DOWN.The node is also removed from the `seen` table.
        /// The node will eventually be removed by the leader, and only after removal a new node with same address can
        /// join the cluster through the normal joining procedure.
        /// </summary>
        public void Downing(Address address)
        {
            var localGossip = _latestGossip;
            var localMembers = localGossip.Members;
            var localOverview = localGossip.Overview;
            var localSeen = localOverview.Seen;
            var localReachability = localOverview.Reachability;

            // check if the node to DOWN is in the 'members' set
            var member = localMembers.FirstOrDefault(m => m.Address == address);
            if (member != null && member.Status != MemberStatus.Down)
            {
                if (localReachability.IsReachable(member.UniqueAddress))
                    _log.Info("Marking node [{0}] as [{1}]", member.Address, MemberStatus.Down);
                else
                    _log.Info("Marking unreachable node [{0}] as [{1}]", member.Address, MemberStatus.Down);

                // replace member (changed status)
                var newMembers = localMembers.Remove(member).Add(member.Copy(MemberStatus.Down));
                // remove nodes marked as DOWN from the 'seen' table
                var newSeen = localSeen.Remove(member.UniqueAddress);

                //update gossip overview
                var newOverview = localOverview.Copy(seen: newSeen);
                var newGossip = localGossip.Copy(members: newMembers, overview: newOverview); //update gossip
                UpdateLatestGossip(newGossip);

                Publish(_latestGossip);
            }
            else if (member != null)
            {
                // already down
            }
            else
            {
                _log.Info("Ignoring down of unknown node [{0}]", address);
            }
        }

        public void Quarantined(UniqueAddress node)
        {
            var localGossip = _latestGossip;
            if (localGossip.HasMember(node))
            {
                var newReachability = localGossip.Overview.Reachability.Terminated(SelfUniqueAddress, node);
                var newOverview = localGossip.Overview.Copy(reachability: newReachability);
                var newGossip = localGossip.Copy(overview: newOverview);
                UpdateLatestGossip(newGossip);
                _log.Warning("Cluster Node [{0}] - Marking node as TERMINATED [{1}], due to quarantine. Node roles [{2}]",
                    Self, node.Address, string.Join(",", _cluster.SelfRoles));
                Publish(_latestGossip);
                Downing(node.Address);
            }
        }

        public void ReceiveGossipStatus(GossipStatus status)
        {
            var from = status.From;
            if (!_latestGossip.Overview.Reachability.IsReachable(SelfUniqueAddress, from))
                _log.Info("Ignoring received gossip status from unreachable [{0}]", from);
            else if (_latestGossip.Members.All(m => !m.UniqueAddress.Equals(from)))
                _log.Debug("Cluster Node [{0}] - Ignoring received gossip status from unknown [{1}]",
                    _cluster.SelfAddress, from);
            else
            {
                var comparison = status.Version.CompareTo(_latestGossip.Version);
                switch (comparison)
                {
                    case VectorClock.Ordering.Same:
                        //same version
                        break;
                    case VectorClock.Ordering.After:
                        GossipStatusTo(from, Sender); //remote is newer
                        break;
                    default:
                        GossipTo(from, Sender); //conflicting or local is newer
                        break;
                }
            }
        }

        /// <summary>
        /// The types of gossip actions that receive gossip has performed.
        /// </summary>
        public enum ReceiveGossipType
        {
            Ignored,
            Older,
            Newer,
            Same,
            Merge
        }

        /// <summary>
        /// The types of gossip actions that receive gossip has performed.
        /// </summary>
        public ReceiveGossipType ReceiveGossip(GossipEnvelope envelope)
        {
            var from = envelope.From;
            var remoteGossip = envelope.Gossip;
            var localGossip = _latestGossip;

            if (remoteGossip.Equals(Gossip.Empty))
            {
                _log.Debug("Cluster Node [{0}] - Ignoring received gossip from [{1}] to protect against overload",
                    _cluster.SelfAddress, from);
                return ReceiveGossipType.Ignored;
            }
            if (!envelope.To.Equals(SelfUniqueAddress))
            {
                _log.Info("Ignoring received gossip intended for someone else, from [{0}] to [{1}]",
                    from.Address, envelope.To);
                return ReceiveGossipType.Ignored;
            }
            if (!localGossip.Overview.Reachability.IsReachable(SelfUniqueAddress, from))
            {
                _log.Info("Ignoring received gossip from unreachable [{0}]", from);
                return ReceiveGossipType.Ignored;
            }
            if (localGossip.Members.All(m => !m.UniqueAddress.Equals(from)))
            {
                _log.Debug("Cluster Node [{0}] - Ignoring received gossip from unknown [{1}]", _cluster.SelfAddress, from);
                return ReceiveGossipType.Ignored;
            }
            if (remoteGossip.Members.All(m => !m.UniqueAddress.Equals(SelfUniqueAddress)))
            {
                _log.Debug("Ignoring received gossip that does not contain myself, from [{0}]", from);
                return ReceiveGossipType.Ignored;
            }

            var comparison = remoteGossip.Version.CompareTo(localGossip.Version);

            Gossip winningGossip;
            bool talkback;
            ReceiveGossipType gossipType;

            switch (comparison)
            {
                case VectorClock.Ordering.Same:
                    //same version
                    winningGossip = remoteGossip.MergeSeen(localGossip);
                    talkback = !remoteGossip.SeenByNode(SelfUniqueAddress);
                    gossipType = ReceiveGossipType.Same;
                    break;
                case VectorClock.Ordering.Before:
                    //local is newer
                    winningGossip = localGossip;
                    talkback = true;
                    gossipType = ReceiveGossipType.Older;
                    break;
                case VectorClock.Ordering.After:
                    //remote is newer
                    winningGossip = remoteGossip;
                    talkback = !remoteGossip.SeenByNode(SelfUniqueAddress);
                    gossipType = ReceiveGossipType.Newer;
                    break;
                default:
                    var prunedLocalGossip = localGossip.Members.Aggregate(localGossip, (g, m) =>
                    {
                        if (Gossip.RemoveUnreachableWithMemberStatus.Contains(m.Status) && !remoteGossip.Members.Contains(m))
                        {
                            _log.Debug("Cluster Node [{0}] - Pruned conflicting local gossip: {1}", _cluster.SelfAddress, m);
                            return g.Prune(VectorClock.Node.Create(VclockName(m.UniqueAddress)));
                        }
                        return g;
                    });

                    var prunedRemoteGossip = remoteGossip.Members.Aggregate(remoteGossip, (g, m) =>
                    {
                        if (Gossip.RemoveUnreachableWithMemberStatus.Contains(m.Status) && !localGossip.Members.Contains(m))
                        {
                            _log.Debug("Cluster Node [{0}] - Pruned conflicting remote gossip: {1}", _cluster.SelfAddress, m);
                            return g.Prune(VectorClock.Node.Create(VclockName(m.UniqueAddress)));
                        }
                        return g;
                    });

                    //conflicting versions, merge
                    winningGossip = prunedRemoteGossip.Merge(prunedLocalGossip);
                    talkback = true;
                    gossipType = ReceiveGossipType.Merge;
                    break;
            }

            _latestGossip = winningGossip.Seen(SelfUniqueAddress);
            AssertLatestGossip();

            // for all new joining nodes we remove them from the failure detector
            foreach (var node in _latestGossip.Members)
            {
                if (node.Status == MemberStatus.Joining && !localGossip.Members.Contains(node))
                    _cluster.FailureDetector.Remove(node.Address);
            }

            _log.Debug("Cluster Node [{0}] - Receiving gossip from [{1}]", _cluster.SelfAddress, from);

            if (comparison == VectorClock.Ordering.Concurrent)
            {
                _log.Debug(@"""Couldn't establish a causal relationship between ""remote"" gossip and ""local"" gossip - Remote[{0}] - Local[{1}] - merged them into [{2}]""",
                    remoteGossip, localGossip, winningGossip);
            }

            if (_statsEnabled)
            {
                switch (gossipType)
                {
                    case ReceiveGossipType.Merge:
                        _gossipStats = _gossipStats.IncrementMergeCount();
                        break;
                    case ReceiveGossipType.Same:
                        _gossipStats = _gossipStats.IncrementSameCount();
                        break;
                    case ReceiveGossipType.Newer:
                        _gossipStats = _gossipStats.IncrementNewerCount();
                        break;
                    case ReceiveGossipType.Older:
                        _gossipStats = _gossipStats.IncrementOlderCount();
                        break;
                }
            }

            Publish(_latestGossip);

            var selfStatus = _latestGossip.GetMember(SelfUniqueAddress).Status;
            if (selfStatus == MemberStatus.Exiting)
            {
                Shutdown();
            }
            else if (talkback)
            {
                // send back gossip to sender() when sender() had different view, i.e. merge, or sender() had
                // older or sender() had newer
                GossipTo(from, Sender);
            }
            return gossipType;
        }

        public void GossipTick()
        {
            SendGossip();
            if (IsGossipSpeedupNeeded())
            {
                _cluster.Scheduler.ScheduleOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks / 3), Self,
                    InternalClusterAction.GossipSpeedupTick.Instance);
                _cluster.Scheduler.ScheduleOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks * 2 / 3), Self,
                    InternalClusterAction.GossipSpeedupTick.Instance);
            }
        }

        public void GossipSpeedupTick()
        {
            if (IsGossipSpeedupNeeded()) SendGossip();
        }

        public bool IsGossipSpeedupNeeded()
        {
            return _latestGossip.Overview.Seen.Count < _latestGossip.Members.Count / 2;
        }

        /// <summary>
        /// Initiates a new round of gossip.
        /// </summary>
        public void SendGossip()
        {
            if (!IsSingletonCluster)
            {
                var localGossip = _latestGossip;

                ImmutableList<UniqueAddress> preferredGossipTarget;

                if (ThreadLocalRandom.Current.NextDouble() < AdjustedGossipDifferentViewProbability)
                {
                    // If it's time to try to gossip to some nodes with a different view
                    // gossip to a random alive member with preference to a member with older gossip version
                    preferredGossipTarget = ImmutableList.CreateRange(localGossip.Members.Where(m => !localGossip.SeenByNode(m.UniqueAddress) &&
                                                   ValidNodeForGossip(m.UniqueAddress)).Select(m => m.UniqueAddress));
                }
                else
                {
                    preferredGossipTarget = ImmutableList<UniqueAddress>.Empty;
                }

                if (!preferredGossipTarget.IsEmpty)
                {
                    var peer = SelectRandomNode(preferredGossipTarget);
                    // send full gossip because it has different view
                    GossipTo(peer);
                }
                else
                {
                    // Fall back to localGossip; important to preserve the original order
                    var peer =
                        SelectRandomNode(
                            ImmutableList.CreateRange(
                                localGossip.Members.Where(m => ValidNodeForGossip(m.UniqueAddress))
                                    .Select(m => m.UniqueAddress)));

                    if (peer != null)
                    {
                        if (localGossip.SeenByNode(peer)) GossipStatusTo(peer);
                        else GossipTo(peer);
                    }
                }
            }
        }

        /// <summary>
        /// For large clusters we should avoid shooting down individual
        /// nodes. Therefore the probability is reduced for large clusters
        /// </summary>
        public double AdjustedGossipDifferentViewProbability
        {
            get
            {
                var size = _latestGossip.Members.Count;
                var low = _cluster.Settings.ReduceGossipDifferentViewProbability;
                var high = low * 3;
                // start reduction when cluster is larger than configured ReduceGossipDifferentViewProbability
                if (size <= low)
                    return _cluster.Settings.GossipDifferentViewProbability;

                // don't go lower than 1/10 of the configured GossipDifferentViewProbability
                var minP = _cluster.Settings.GossipDifferentViewProbability / 10;
                if (size >= high) return minP;
                else
                {
                    // linear reduction of the probability with increasing number of nodes
                    // from ReduceGossipDifferentViewProbability at ReduceGossipDifferentViewProbability nodes
                    // to ReduceGossipDifferentViewProbability / 10 at ReduceGossipDifferentViewProbability * 3 nodes
                    // i.e. default from 0.8 at 400 nodes, to 0.08 at 1600 nodes     
                    var k = (minP - _cluster.Settings.GossipDifferentViewProbability) / (high - low);
                    return _cluster.Settings.GossipDifferentViewProbability + (size - low) * k;
                }
            }
        }

        /// <summary>
        /// Runs periodic leader actions, such as member status transitions, assigning partitions etc.
        /// </summary>
        public void LeaderActions()
        {
            if (_latestGossip.IsLeader(SelfUniqueAddress, SelfUniqueAddress))
            {
                // only run the leader actions if we are the LEADER
                const int firstNotice = 20;
                const int periodicNotice = 60;
                if (_latestGossip.Convergence(SelfUniqueAddress))
                {
                    if (_leaderActionCounter >= firstNotice)
                        _log.Info("Leader can perform its duties again");
                    _leaderActionCounter = 0;
                    LeaderActionsOnConvergence();
                }
                else
                {
                    _leaderActionCounter += 1;
                    if (_leaderActionCounter == firstNotice || _leaderActionCounter % periodicNotice == 0)
                    {
                        _log.Info(
                            "Leader can currently not perform its duties, reachability status: [{0}], member status: [{1}]",
                            _latestGossip.ReachabilityExcludingDownedObservers,
                            string.Join(", ", _latestGossip.Members
                                .Select(m => string.Format("${0} ${1} seen=${2}",
                                    m.Address,
                                    m.Status,
                                    _latestGossip.SeenByNode(m.UniqueAddress)))));
                    }
                }
            }
            ShutdownSelfWhenDown();
        }

        private void ShutdownSelfWhenDown()
        {
            if (_latestGossip.GetMember(SelfUniqueAddress).Status == MemberStatus.Down)
            {
                // When all reachable have seen the state this member will shutdown itself when it has
                // status Down. The down commands should spread before we shutdown.
                var unreachable = _latestGossip.Overview.Reachability.AllUnreachableOrTerminated;
                var downed = _latestGossip.Members.Where(m => m.Status == MemberStatus.Down)
                    .Select(m => m.UniqueAddress).ToList();
                if (downed.All(node => unreachable.Contains(node) || _latestGossip.SeenByNode(node)))
                {
                    // the reason for not shutting down immediately is to give the gossip a chance to spread
                    // the downing information to other downed nodes, so that they can shutdown themselves
                    _log.Info("Shutting down myself");
                    downed
                        .Where(n => !unreachable.Contains(n) || n == SelfUniqueAddress)
                        .Take(MaxGossipsBeforeShuttingDownMyself)
                        .ForEach(GossipTo);

                    Shutdown();
                }
            }
        }

        public bool IsMinNrOfMembersFulfilled()
        {
            return _latestGossip.Members.Count >= _cluster.Settings.MinNrOfMembers
                && _cluster.Settings
                    .MinNrOfMembersOfRole
                    .All(x => _latestGossip.Members.Count(c => c.HasRole(x.Key)) >= x.Value);
        }

        /// <summary>
        ///  Leader actions are as follows:
        /// 1. Move JOINING     => UP                   -- When a node joins the cluster
        /// 2. Move LEAVING     => EXITING              -- When all partition handoff has completed
        /// 3. Non-exiting remain                       -- When all partition handoff has completed
        /// 4. Move unreachable EXITING => REMOVED      -- When all nodes have seen the EXITING node as unreachable (convergence) -
        ///                                                 remove the node from the node ring and seen table
        /// 5. Move unreachable DOWN/EXITING => REMOVED -- When all nodes have seen that the node is DOWN/EXITING (convergence) -
        ///                                                 remove the node from the node ring and seen table
        /// 7. Updating the vclock version for the changes
        /// 8. Updating the `seen` table
        /// 9. Update the state with the new gossip
        /// </summary>
        public void LeaderActionsOnConvergence()
        {
            var localGossip = _latestGossip;
            var localMembers = localGossip.Members;
            var localOverview = localGossip.Overview;
            var localSeen = localOverview.Seen;

            bool enoughMembers = IsMinNrOfMembersFulfilled();
            Func<Member, bool> isJoiningUp = m => m.Status == MemberStatus.Joining && enoughMembers;

            var removedUnreachable =
                localOverview.Reachability.AllUnreachableOrTerminated.Select(localGossip.GetMember)
                    .Where(m => Gossip.RemoveUnreachableWithMemberStatus.Contains(m.Status))
                    .ToImmutableHashSet();

            var changedMembers = localMembers.Select(m =>
            {
                var upNumber = 0;

                if (isJoiningUp(m))
                {
                    // Move JOINING => UP (once all nodes have seen that this node is JOINING, i.e. we have a convergence)
                    // and minimum number of nodes have joined the cluster
                    if (upNumber == 0)
                    {
                        // It is alright to use same upNumber as already used by a removed member, since the upNumber
                        // is only used for comparing age of current cluster members (Member.isOlderThan)
                        var youngest = localGossip.YoungestMember;
                        upNumber = 1 + (youngest.UpNumber == int.MaxValue ? 0 : youngest.UpNumber);
                    }
                    else
                    {
                        upNumber += 1;
                    }
                    return m.CopyUp(upNumber);
                }

                if (m.Status == MemberStatus.Leaving)
                {
                    // Move LEAVING => EXITING (once we have a convergence on LEAVING
                    // *and* if we have a successful partition handoff)
                    return m.Copy(MemberStatus.Exiting);
                }

                return null;
            }).Where(m => m != null).ToImmutableSortedSet();

            if (!removedUnreachable.IsEmpty || !changedMembers.IsEmpty)
            {
                // handle changes

                // replace changed members
                var newMembers = changedMembers
                    .Union(localMembers)
                    .Except(removedUnreachable);

                // removing REMOVED nodes from the `seen` table
                var removed = removedUnreachable.Select(u => u.UniqueAddress).ToImmutableHashSet();
                var newSeen = localSeen.Except(removed);
                // removing REMOVED nodes from the `reachability` table
                var newReachability = localOverview.Reachability.Remove(removed);
                var newOverview = localOverview.Copy(seen: newSeen, reachability: newReachability);
                // Clear the VectorClock when member is removed. The change made by the leader is stamped
                // and will propagate as is if there are no other changes on other nodes.
                // If other concurrent changes on other nodes (e.g. join) the pruning is also
                // taken care of when receiving gossips.
                var newVersion = removed.Aggregate(localGossip.Version, (v, node) =>
                {
                    return v.Prune(VectorClock.Node.Create(VclockName(node)));
                });
                var newGossip = localGossip.Copy(members: newMembers, overview: newOverview, version: newVersion);

                UpdateLatestGossip(newGossip);

                // log status changes
                foreach (var m in changedMembers)
                    _log.Info("Leader is moving node [{0}] to [{1}]", m.Address, m.Status);

                //log the removal of unreachable nodes
                foreach (var m in removedUnreachable)
                {
                    var status = m.Status == MemberStatus.Exiting ? "exiting" : "unreachable";
                    _log.Info("Leader is removing {0} node [{1}]", status, m.Address);
                }

                Publish(_latestGossip);

                if (_latestGossip.GetMember(SelfUniqueAddress).Status == MemberStatus.Exiting)
                {
                    // Leader is moving itself from Leaving to Exiting. Let others know (best effort)
                    // before shutdown. Otherwise they will not see the Exiting state change
                    // and there will not be convergence until they have detected this node as
                    // unreachable and the required downing has finished. They will still need to detect
                    // unreachable, but Exiting unreachable will be removed without downing, i.e.
                    // normally the leaving of a leader will be graceful without the need
                    // for downing. However, if those final gossip messages never arrive it is
                    // alright to require the downing, because that is probably caused by a
                    // network failure anyway.
                    for (var i = 1; i <= NumberOfGossipsBeforeShutdownWhenLeaderExits; i++)
                    {
                        SendGossip();
                    }
                    Shutdown();
                }
            }
        }

        /// <summary>
        /// Reaps the unreachable members according to the failure detector's verdict.
        /// </summary>
        public void ReapUnreachableMembers()
        {
            if (!IsSingletonCluster)
            {
                // only scrutinize if we are a non-singleton cluster

                var localGossip = _latestGossip;
                var localOverview = localGossip.Overview;
                var localMembers = localGossip.Members;

                var newlyDetectedUnreachableMembers =
                    localMembers.Where(member => !(
                        member.UniqueAddress.Equals(SelfUniqueAddress) ||
                        localOverview.Reachability.Status(SelfUniqueAddress, member.UniqueAddress) == Reachability.ReachabilityStatus.Unreachable ||
                        localOverview.Reachability.Status(SelfUniqueAddress, member.UniqueAddress) == Reachability.ReachabilityStatus.Terminated ||
                        _cluster.FailureDetector.IsAvailable(member.Address))).ToImmutableSortedSet();

                var newlyDetectedReachableMembers = localOverview.Reachability.AllUnreachableFrom(SelfUniqueAddress)
                        .Where(node => !node.Equals(SelfUniqueAddress) && _cluster.FailureDetector.IsAvailable(node.Address))
                        .Select(localGossip.GetMember).ToImmutableHashSet();

                if (!newlyDetectedUnreachableMembers.IsEmpty || !newlyDetectedReachableMembers.IsEmpty)
                {
                    var newReachability1 = newlyDetectedUnreachableMembers.Aggregate(
                        localOverview.Reachability,
                        (reachability, m) => reachability.Unreachable(SelfUniqueAddress, m.UniqueAddress));

                    var newReachability2 = newlyDetectedReachableMembers.Aggregate(
                        newReachability1,
                        (reachability, m) => reachability.Reachable(SelfUniqueAddress, m.UniqueAddress));

                    if (!newReachability2.Equals(localOverview.Reachability))
                    {
                        var newOverview = localOverview.Copy(reachability: newReachability2);
                        var newGossip = localGossip.Copy(overview: newOverview);

                        UpdateLatestGossip(newGossip);

                        var partitioned = newlyDetectedUnreachableMembers.Partition(m => m.Status == MemberStatus.Exiting);
                        var exiting = partitioned.Item1;
                        var nonExiting = partitioned.Item2;

                        if (!nonExiting.IsEmpty)
                            _log.Warning("Cluster Node [{0}] - Marking node(s) as UNREACHABLE [{1}]. Node roles [{2}]",
                                _cluster.SelfAddress, nonExiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b), string.Join(",", _cluster.SelfRoles));

                        if (!exiting.IsEmpty)
                            _log.Warning("Marking exiting node(s) as UNREACHABLE [{0}]. This is expected and they will be removed.",
                                _cluster.SelfAddress, exiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b));

                        if (!newlyDetectedReachableMembers.IsEmpty)
                            _log.Info("Marking node(s) as REACHABLE [{0}]. Node roles [{1}]", newlyDetectedReachableMembers.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b), string.Join(",", _cluster.SelfRoles));

                        Publish(_latestGossip);
                    }
                }
            }
        }

        public UniqueAddress SelectRandomNode(ImmutableList<UniqueAddress> nodes)
        {
            if (nodes.IsEmpty) return null;
            return nodes[ThreadLocalRandom.Current.Next(nodes.Count)];
        }

        public bool IsSingletonCluster
        {
            get { return _latestGossip.IsSingletonCluster; }
        }

        /// <summary>
        /// needed for tests
        /// </summary>
        public void SendGossipTo(Address address)
        {
            foreach (var m in _latestGossip.Members)
            {
                if (m.Address.Equals(address))
                    GossipTo(m.UniqueAddress);
            }
        }

        /// <summary>
        /// Gossips latest gossip to a node.
        /// </summary>
        public void GossipTo(UniqueAddress node)
        {
            if (ValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipEnvelope(SelfUniqueAddress, node, _latestGossip));
        }

        public void GossipTo(UniqueAddress node, IActorRef destination)
        {
            if (ValidNodeForGossip(node))
                destination.Tell(new GossipEnvelope(SelfUniqueAddress, node, _latestGossip));
        }

        public void GossipStatusTo(UniqueAddress node)
        {
            if (ValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipStatus(SelfUniqueAddress, _latestGossip.Version));
        }

        public void GossipStatusTo(UniqueAddress node, IActorRef destination)
        {
            if (ValidNodeForGossip(node))
                destination.Tell(new GossipStatus(SelfUniqueAddress, _latestGossip.Version));
        }

        public bool ValidNodeForGossip(UniqueAddress node)
        {
            return !node.Equals(SelfUniqueAddress) && _latestGossip.HasMember(node) &&
                    _latestGossip.ReachabilityExcludingDownedObservers.Value.IsReachable(node);
        }

        public void UpdateLatestGossip(Gossip newGossip)
        {
            // Updating the vclock version for the changes
            var versionedGossip = newGossip.Increment(_vclockNode);
            // Nobody else have seen this gossip but us
            var seenVersionedGossip = versionedGossip.OnlySeen(SelfUniqueAddress);
            // Update the state with the new gossip
            _latestGossip = seenVersionedGossip;
            AssertLatestGossip();
        }

        public void AssertLatestGossip()
        {
            if (Cluster.IsAssertInvariantsEnabled && _latestGossip.Version.Versions.Count > _latestGossip.Members.Count)
            {
                throw new InvalidOperationException($"Too many vector clock entries in gossip state {_latestGossip}");
            }
        }

        public void Publish(Gossip newGossip)
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(newGossip));
            if (_cluster.Settings.PublishStatsInterval == TimeSpan.Zero)
            {
                PublishInternalStats();
            }
        }

        public void PublishInternalStats()
        {
            var vclockStats = new VectorClockStats(_latestGossip.Version.Versions.Count,
                _latestGossip.Members.Count(m => _latestGossip.SeenByNode(m.UniqueAddress)));

            _publisher.Tell(new ClusterEvent.CurrentInternalStats(_gossipStats, vclockStats));
        }

        readonly ILoggingAdapter _log = Context.GetLogger();
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Sends <see cref="InternalClusterAction.InitJoin"/> to all seed nodes (except itself) and expect
    /// <see cref="InternalClusterAction.InitJoinAck"/> reply back. The seed node that replied first
    /// will be used and joined to. <see cref="InternalClusterAction.InitJoinAck"/> replies received after
    /// the first one are ignored.
    /// 
    /// Retries if no <see cref="InternalClusterAction.InitJoinAck"/> replies are received within the 
    /// <see cref="ClusterSettings.SeedNodeTimeout"/>. When at least one reply has been received it stops itself after
    /// an idle <see cref="ClusterSettings.SeedNodeTimeout"/>.
    /// 
    /// The seed nodes can be started in any order, but they will not be "active" until they have been
    /// able to join another seed node (seed1.)
    /// 
    /// They will retry the join procedure.
    /// 
    /// Possible scenarios:
    ///  1. seed2 started, but doesn't get any ack from seed1 or seed3
    ///  2. seed3 started, doesn't get any ack from seed1 or seed3 (seed2 doesn't reply)
    ///  3. seed1 is started and joins itself
    ///  4. seed2 retries the join procedure and gets an ack from seed1, and then joins to seed1
    ///  5. seed3 retries the join procedure and gets acks from seed2 first, and then joins to seed2
    /// </summary>
    internal sealed class JoinSeedNodeProcess : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly ImmutableList<Address> _seeds;
        private readonly Address _selfAddress;

        public JoinSeedNodeProcess(ImmutableList<Address> seeds)
        {
            _selfAddress = Cluster.Get(Context.System).SelfAddress;
            _seeds = seeds;
            if (seeds.IsEmpty || seeds.Head() == _selfAddress)
                throw new ArgumentException("Join seed node should not be done");
            Context.SetReceiveTimeout(Cluster.Get(Context.System).Settings.SeedNodeTimeout);
        }

        protected override void PreStart()
        {
            Self.Tell(new InternalClusterAction.JoinSeenNode());
        }

        protected override void OnReceive(object message)
        {
            if (message is InternalClusterAction.JoinSeenNode)
            {
                //send InitJoin to all seed nodes (except myself)
                foreach (var path in _seeds.Where(x => x != _selfAddress)
                            .Select(y => Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(y))))
                {
                    path.Tell(new InternalClusterAction.InitJoin());
                }
            }
            else if (message is InternalClusterAction.InitJoinAck)
            {
                //first InitJoinAck reply
                var initJoinAck = (InternalClusterAction.InitJoinAck)message;
                Context.Parent.Tell(new ClusterUserAction.JoinTo(initJoinAck.Address));
                Context.Become(Done);
            }
            else if (message is InternalClusterAction.InitJoinNack) { } //that seed was uninitialized
            else if (message is ReceiveTimeout)
            {
                //no InitJoinAck received - try again
                Self.Tell(new InternalClusterAction.JoinSeenNode());
            }
            else
            {
                Unhandled(message);
            }
        }

        private void Done(object message)
        {
            if (message is InternalClusterAction.InitJoinAck)
            {
                //already received one, skip the rest
            }
            else if (message is ReceiveTimeout) Context.Stop(Self);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Used only for the first seed node.
    /// Sends <see cref="InternalClusterAction.InitJoin"/> to all seed nodes except itself.
    /// If other seed nodes are not part of the cluster yet they will reply with 
    /// <see cref="InternalClusterAction.InitJoinNack"/> or not respond at all and then the
    /// first seed node will join itself to initialize the new cluster. When the first seed 
    /// node is restarted, and some other seed node is part of the cluster it will reply with
    /// <see cref="InternalClusterAction.InitJoinAck"/> and then the first seed node will
    /// join that other seed node to join the existing cluster.
    /// </summary>
    internal sealed class FirstSeedNodeProcess : UntypedActor
    {
        readonly ILoggingAdapter _log = Context.GetLogger();

        private ImmutableList<Address> _remainingSeeds;
        readonly Address _selfAddress;
        readonly Cluster _cluster;
        readonly Deadline _timeout;
        readonly ICancelable _retryTaskToken;

        public FirstSeedNodeProcess(ImmutableList<Address> seeds)
        {
            _cluster = Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;

            if (seeds.Count <= 1 || seeds.Head() != _selfAddress)
                throw new ArgumentException("Join seed node should not be done");

            _remainingSeeds = seeds.Remove(_selfAddress);
            _timeout = Deadline.Now + _cluster.Settings.SeedNodeTimeout;
            _retryTaskToken = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Self, new InternalClusterAction.JoinSeenNode(), Self);
            Self.Tell(new InternalClusterAction.JoinSeenNode());
        }

        protected override void PostStop()
        {
            _retryTaskToken.Cancel();
        }

        protected override void OnReceive(object message)
        {
            if (message is InternalClusterAction.JoinSeenNode)
            {
                if (_timeout.HasTimeLeft)
                {
                    // send InitJoin to remaining seed nodes (except myself)
                    foreach (var seed in _remainingSeeds.Select(
                                x => Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(x))))
                        seed.Tell(new InternalClusterAction.InitJoin());
                }
                else
                {
                    // no InitJoinAck received, initialize new cluster by joining myself
                    Context.Parent.Tell(new ClusterUserAction.JoinTo(_selfAddress));
                    Context.Stop(Self);
                }
            }
            else if (message is InternalClusterAction.InitJoinAck)
            {
                // first InitJoinAck reply, join existing cluster
                var initJoinAck = (InternalClusterAction.InitJoinAck)message;
                Context.Parent.Tell(new ClusterUserAction.JoinTo(initJoinAck.Address));
                Context.Stop(Self);
            }
            else if (message is InternalClusterAction.InitJoinNack)
            {
                var initJoinNack = (InternalClusterAction.InitJoinNack)message;
                _remainingSeeds = _remainingSeeds.Remove(initJoinNack.Address);
                if (_remainingSeeds.IsEmpty)
                {
                    // initialize new cluster by joining myself when nacks from all other seed nodes
                    Context.Parent.Tell(new ClusterUserAction.JoinTo(_selfAddress));
                    Context.Stop(Self);
                }
            }
            else
            {
                Unhandled(message);
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class GossipStats
    {
        public readonly long ReceivedGossipCount;
        public readonly long MergeCount;
        public readonly long SameCount;
        public readonly long NewerCount;
        public readonly long OlderCount;

        public GossipStats(long receivedGossipCount = 0L,
            long mergeCount = 0L,
            long sameCount = 0L,
            long newerCount = 0L, long olderCount = 0L)
        {
            ReceivedGossipCount = receivedGossipCount;
            MergeCount = mergeCount;
            SameCount = sameCount;
            NewerCount = newerCount;
            OlderCount = olderCount;
        }

        public GossipStats IncrementMergeCount()
        {
            return Copy(mergeCount: MergeCount + 1, receivedGossipCount: ReceivedGossipCount + 1);
        }

        public GossipStats IncrementSameCount()
        {
            return Copy(sameCount: SameCount + 1, receivedGossipCount: ReceivedGossipCount + 1);
        }

        public GossipStats IncrementNewerCount()
        {
            return Copy(newerCount: NewerCount + 1, receivedGossipCount: ReceivedGossipCount + 1);
        }

        public GossipStats IncrementOlderCount()
        {
            return Copy(olderCount: OlderCount + 1, receivedGossipCount: ReceivedGossipCount + 1);
        }

        public GossipStats Copy(long? receivedGossipCount = null,
            long? mergeCount = null,
            long? sameCount = null,
            long? newerCount = null, long? olderCount = null)
        {
            return new GossipStats(receivedGossipCount ?? ReceivedGossipCount,
                mergeCount ?? MergeCount,
                sameCount ?? SameCount,
                newerCount ?? NewerCount,
                olderCount ?? OlderCount);
        }

        #region Operator overloads

        public static GossipStats operator +(GossipStats a, GossipStats b)
        {
            return new GossipStats(a.ReceivedGossipCount + b.ReceivedGossipCount,
                a.MergeCount + b.MergeCount,
                a.SameCount + b.SameCount,
                a.NewerCount + b.NewerCount,
                a.OlderCount + b.OlderCount);
        }

        public static GossipStats operator -(GossipStats a, GossipStats b)
        {
            return new GossipStats(a.ReceivedGossipCount - b.ReceivedGossipCount,
                a.MergeCount - b.MergeCount,
                a.SameCount - b.SameCount,
                a.NewerCount - b.NewerCount,
                a.OlderCount - b.OlderCount);
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The supplied callback will be run once when the current cluster member has the same status.
    /// </summary>
    internal class OnMemberStatusChangedListener : ReceiveActor
    {
        private readonly Action _callback;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Cluster _cluster;
        private readonly MemberStatus _targetStatus;


        public OnMemberStatusChangedListener(Action callback, MemberStatus targetStatus)
        {
            _targetStatus = targetStatus;
            _callback = callback;
            _cluster = Cluster.Get(Context.System);

            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                if (state.Members.Any(IsTriggered))
                    Done();
            });

            Receive<ClusterEvent.MemberUp>(up =>
            {
                if (IsTriggered(up.Member))
                    Done();
            });

            Receive<ClusterEvent.MemberRemoved>(removed =>
            {
                if (IsTriggered(removed.Member))
                    Done();
            });
        }

        protected override void PreStart()
        {
            var type = _targetStatus == MemberStatus.Up
                ? typeof(ClusterEvent.MemberUp)
                : typeof(ClusterEvent.MemberRemoved);

            _cluster.Subscribe(Self, new[] { type });
        }

        protected override void PostStop()
        {
            if (_targetStatus == MemberStatus.Removed)
                Done();
            _cluster.Unsubscribe(Self);
        }

        private void Done()
        {
            try
            {
                _callback.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[{0}] callback failed with [{1}]", _targetStatus, ex.Message);
            }
            finally
            {
                Context.Stop(Self);
            }
        }

        private bool IsTriggered(Member m)
        {
            return m.UniqueAddress == _cluster.SelfUniqueAddress && m.Status == _targetStatus;
        }
    }

    internal sealed class VectorClockStats
    {
        public VectorClockStats(int versionSize = 0, int seenLatest = 0)
        {
            VersionSize = versionSize;
            SeenLatest = seenLatest;
        }

        public int VersionSize { get; }
        public int SeenLatest { get; }

        public override bool Equals(object obj)
        {
            var other = obj as VectorClockStats;
            if (other == null) return false;
            return VersionSize == other.VersionSize &&
                   SeenLatest == other.SeenLatest;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (VersionSize * 397) ^ SeenLatest;
            }
        }
    }
}
