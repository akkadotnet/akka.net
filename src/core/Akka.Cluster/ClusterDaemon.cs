using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
                var baseUserAction = (BaseClusterUserAction) obj;
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
            public JoinTo(Address address) : base(address)
            {
            }
        }

        /// <summary>
        /// Command to leave the cluster.
        /// </summary>
        internal sealed class Leave : BaseClusterUserAction, IClusterMessage
        {
            public Leave(Address address) : base(address)
            {}
        }

        /// <summary>
        /// Command to mark node as temporary down.
        /// </summary>
        internal sealed class Down : BaseClusterUserAction, IClusterMessage
        {
            public Down(Address address) : base(address)
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
                return obj is Join && Equals((Join) obj);
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
                return obj is Welcome && Equals((Welcome) obj);
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
        internal interface ITick {}

        internal class GossipTick : ITick
        {
            private GossipTick(){}
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
            private GossipSpeedupTick(){}
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
            private ReapUnreachableTick(){}
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
            private MetricsTick(){}
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
            private LeaderActionsTick(){}
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
            private PublishStatsTick(){}
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

            public Address Address {get { return _address; }}

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
            private GetClusterCoreRef(){}
            private static readonly GetClusterCoreRef _instance = new GetClusterCoreRef();
            public static GetClusterCoreRef Instance
            {
                get
                {
                    return _instance;
                }
            } 
        }

        internal sealed class PublisherCreated
        {
            readonly ActorRef _publisher;

            public PublisherCreated(ActorRef publisher)
            {
                _publisher = publisher;
            }

            public ActorRef Publisher
            {
                get { return _publisher; }
            }
        }

        /// <summary>
        /// Command to <see cref="Akka.Cluster.ClusterDaemon"/> to create a
        /// <see cref="Akka.Cluster.OnMemberUpListener"/>
        /// </summary>
        public sealed class AddOnMemberUpListener : NoSerializationVerificationNeeded
        {
            readonly Action _callback;

            public AddOnMemberUpListener(Action callback)
            {
                _callback = callback;
            }

            public Action Callback
            {
                get { return _callback; }
            }
        }

        public interface ISubscriptionMessage { }

        public sealed class Subscribe : ISubscriptionMessage
        {
            readonly ActorRef _subscriber;
            readonly ClusterEvent.SubscriptionInitialStateMode _initialStateMode;
            readonly ImmutableHashSet<Type> _to;

            public Subscribe(ActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode,
                ImmutableHashSet<Type> to)
            {
                _subscriber = subscriber;
                _initialStateMode = initialStateMode;
                _to = to;
            }

            public ActorRef Subscriber
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
            readonly ActorRef _subscriber;
            readonly Type _to;

            public Unsubscribe(ActorRef subscriber, Type to)
            {
                _to = to;
                _subscriber = subscriber;
            }

            public ActorRef Subscriber
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
            readonly ActorRef _receiver;

            public ActorRef Receiver
            {
                get { return _receiver; }
            }

            /// <param name="receiver"><see cref="Akka.Cluster.ClusterEvent.CurrentClusterState"/> will be sent to the `receiver`</param>
            public SendCurrentClusterState(ActorRef receiver)
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

    //TODO: RequiresMessageQueue?
    /// <summary>
    /// Supervisor managing the different Cluster daemons.
    /// </summary>
    internal sealed class ClusterDaemon : UntypedActor, IActorLogging
    {
        readonly ActorRef _coreSupervisor;
        readonly ClusterSettings _settings;

        public ClusterDaemon(ClusterSettings settings)
        {
            // Important - don't use Cluster(context.system) here because that would
            // cause deadlock. The Cluster extension is currently being created and is waiting
            // for response from GetClusterCoreRef in its constructor.
            _coreSupervisor =
                Context.ActorOf(Props.Create<ClusterCoreSupervisor>().WithDispatcher(Context.Props.Dispatcher), "core");

            Context.ActorOf(Props.Create<ClusterHeartbeatReceiver>().WithDispatcher(Context.Props.Dispatcher), "heartbeatReceiver");

            _settings = settings;
        }

        protected override void OnReceive(object message)
        {
            message.Match()
                .With<InternalClusterAction.GetClusterCoreRef>(msg => _coreSupervisor.Forward(msg))
                .With<InternalClusterAction.AddOnMemberUpListener>(
                    msg =>
                        Context.ActorOf(Props.Create(() => new OnMemberUpListener(msg.Callback)).WithDeploy(Deploy.Local)))
                .With<InternalClusterAction.PublisherCreated>(
                    msg =>
                    {
                        if (_settings.MetricsEnabled)
                            Context.ActorOf(
                                Props.Create<ClusterHeartbeatReceiver>().WithDispatcher(Context.Props.Dispatcher),
                                "metrics");
                    });
        }

        private readonly LoggingAdapter _log = Context.GetLogger();
        public LoggingAdapter Log { get { return _log; } }
    }

    /// <summary>
    /// ClusterCoreDaemon and ClusterDomainEventPublisher can't be restarted because the state
    /// would be obsolete. Shutdown the member if any those actors crashed.
    /// </summary>
    class ClusterCoreSupervisor : ReceiveActor, IActorLogging
    {
        readonly ActorRef _publisher;
        readonly ActorRef _coreDaemon;

        private readonly LoggingAdapter _log = Context.GetLogger();
        public LoggingAdapter Log { get { return _log; } }

        public ClusterCoreSupervisor()
        {
            _publisher =
                Context.ActorOf(Props.Create<ClusterDomainEventPublisher>().WithDispatcher(Context.Props.Dispatcher), "publisher");
            _coreDaemon = Context.ActorOf(new Props(typeof(ClusterCoreDaemon), new object[]{_publisher}).WithDispatcher(Context.Props.Dispatcher), "daemon");
            Context.Watch(_coreDaemon);

            Context.Parent.Tell(new InternalClusterAction.PublisherCreated(_publisher));

            Receive<InternalClusterAction.GetClusterCoreRef>(cr => Sender.Tell(_coreDaemon));
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(e =>
            {
                //TODO: JVM version matches NonFatal. Can / should we do something similar? 
                Log.Error(e, "Cluster node [{0}] crashed, [{1}] - shutting down...",
                    Cluster.Get(Context.System).SelfAddress, e);
                Self.Tell(PoisonPill.Instance);
                return Directive.Stop;
            });
        }

        protected override void PostStop()
        {
            Cluster.Get(Context.System).Shutdown();
        }
        
    }

    class ClusterCoreDaemon : UntypedActor, IActorLogging
    {
        readonly Cluster _cluster = Cluster.Get(Context.System);
        protected readonly UniqueAddress SelfUniqueAddress;
        const int NumberOfGossipsBeforeShutdownWhenLeaderExits = 3;
        readonly VectorClock.Node _vclockNode;
        string VclockName(UniqueAddress node)
        {
            return node.Address + "-" + node.Uid;
        }
        // note that self is not initially member,
        // and the SendGossip is not versioned for this 'Node' yet
        Gossip _latestGossip = Gossip.Empty;

        readonly bool _statsEnabled;
        GossipStats _gossipStats = new GossipStats();
        ActorRef _seedNodeProcess;
        int _seedNodeProcessCounter = 0; //for unique names

        readonly ActorRef _publisher;

        public ClusterCoreDaemon(ActorRef publisher)
        {
            _publisher = publisher;
            SelfUniqueAddress = _cluster.SelfUniqueAddress;
            _vclockNode  = new VectorClock.Node(VclockName(SelfUniqueAddress));
            //TODO: _statsEnabled = PublishStatsInternal.IsFinite;
            var settings = _cluster.Settings;
            var scheduler = _cluster.Scheduler;

            _gossipTaskCancellable = new CancellationTokenSource();
            _gossipTask =
                scheduler.Schedule(
                    settings.PeriodicTasksInitialDelay.Max(settings.GossipInterval), 
                    settings.GossipInterval,
                    Self, 
                    InternalClusterAction.GossipTick.Instance,
                    _gossipTaskCancellable.Token);

            _failureDetectorReaperTaskCancellable = new CancellationTokenSource();
            _failureDetectorReaperTask =
                scheduler.Schedule(
                    settings.PeriodicTasksInitialDelay.Max(settings.UnreachableNodesReaperInterval),
                    settings.UnreachableNodesReaperInterval, 
                    Self, 
                    InternalClusterAction.ReapUnreachableTick.Instance,
                    _failureDetectorReaperTaskCancellable.Token);

            _leaderActionsTaskCancellable = new CancellationTokenSource();
            _leaderActionsTask =
                scheduler.Schedule(
                    settings.PeriodicTasksInitialDelay.Max(settings.LeaderActionsInterval),
                    settings.LeaderActionsInterval,
                    Self,
                    InternalClusterAction.LeaderActionsTick.Instance,
                    _leaderActionsTaskCancellable.Token);

            if (settings.PublishStatsInterval != null)
            {
                _publishStatsTaskTaskCancellable = new CancellationTokenSource();
                _publishStatsTask =
                            scheduler.Schedule(
                                settings.PeriodicTasksInitialDelay.Max(settings.PublishStatsInterval.Value),
                                settings.PublishStatsInterval.Value,
                                Self,
                                InternalClusterAction.PublishStatsTick.Instance,
                                _publishStatsTaskTaskCancellable.Token);                
            }
        }

        ActorSelection ClusterCore(Address address)
        {
            return Context.ActorSelection(new RootActorPath(address)/"system"/"cluster"/"core"/"daemon");
        }

        readonly Task _gossipTask;
        readonly CancellationTokenSource _gossipTaskCancellable;
        readonly Task _failureDetectorReaperTask;
        readonly CancellationTokenSource _failureDetectorReaperTaskCancellable;
        readonly Task _leaderActionsTask;
        readonly CancellationTokenSource _leaderActionsTaskCancellable;
        readonly Task _publishStatsTask;
        readonly CancellationTokenSource _publishStatsTaskTaskCancellable;

        protected override void PreStart()
        {
            Context.System.EventStream.Subscribe(Self, typeof (QuarantinedEvent));

            if (_cluster.Settings.AutoDownUnreachableAfter != null)
                Context.ActorOf(
                    AutoDown.Props(_cluster.Settings.AutoDownUnreachableAfter.Value).WithDispatcher(Context.Props.Dispatcher),
                    "autoDown");

            if (_cluster.Settings.SeedNodes.IsEmpty)
            {
                Log.Info("No seed-nodes configured, manual cluster join required");
            }
            else
            {
                Self.Tell(new InternalClusterAction.JoinSeedNodes(_cluster.Settings.SeedNodes));
            }
        }

        protected override void PostStop()
        {
            Context.System.EventStream.Unsubscribe(Self);
            _gossipTaskCancellable.Cancel();
            _failureDetectorReaperTaskCancellable.Cancel();
            _leaderActionsTaskCancellable.Cancel();
            if(_publishStatsTaskTaskCancellable != null) _publishStatsTaskTaskCancellable.Cancel();
        }

        private void Uninitialized(object message)
        {
            message.Match()
                .With<InternalClusterAction.InitJoin>(m =>
                    Sender.Tell(new InternalClusterAction.InitJoinAck(_cluster.SelfAddress)))
                .With<ClusterUserAction.JoinTo>(m => Join(m.Address))
                .With<InternalClusterAction.JoinSeedNodes>(m => JoinSeedNodes(m.SeedNodes))
                .With<InternalClusterAction.ISubscriptionMessage>(msg => _publisher.Forward(msg));
        }

        private void TryingToJoin(object message, Address joinWith, Deadline deadline)
        {
            message.Match()
                .With<InternalClusterAction.Welcome>(m => Welcome(joinWith, m.From, m.Gossip))
                .With<InternalClusterAction.InitJoin>(
                    m => Sender.Tell(new InternalClusterAction.InitJoinAck(_cluster.SelfAddress)))
                .With<ClusterUserAction.JoinTo>(m =>
                {
                    BecomeUnitialized();
                    Join(m.Address);
                })
                .With<InternalClusterAction.JoinSeedNodes>(m =>
                {
                    BecomeUnitialized();
                    JoinSeedNodes(m.SeedNodes);
                })
                .With<InternalClusterAction.ISubscriptionMessage>(msg => _publisher.Forward(msg))
                .Default(m =>
                {
                    if (deadline != null && deadline.IsOverdue)
                    {
                        BecomeUnitialized();
                        if (_cluster.Settings.SeedNodes.Any()) JoinSeedNodes(_cluster.Settings.SeedNodes);
                        else Join(joinWith);
                    }
                });
        }

        private void BecomeUnitialized()
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
            message.Match()
                .With<GossipEnvelope>(m => ReceiveGossip(m))
                .With<GossipStatus>(ReceiveGossipStatus)
                .With<InternalClusterAction.GossipTick>(m => GossipTick())
                .With<InternalClusterAction.GossipSpeedupTick>(m => GossipSpeedupTick())
                .With<InternalClusterAction.ReapUnreachableTick>(m => ReapUnreachableMembers())
                .With<InternalClusterAction.LeaderActionsTick>(m => LeaderActions())
                .With<InternalClusterAction.PublishStatsTick>(m => PublishInternalStats())
                .With<InternalClusterAction.InitJoin>(m => InitJoin())
                .With<InternalClusterAction.Join>(m => Joining(m.Node, m.Roles))
                .With<ClusterUserAction.Down>(m => Downing(m.Address))
                .With<ClusterUserAction.Leave>(m => Leaving(m.Address))
                .With<InternalClusterAction.SendGossipTo>(m => SendGossipTo(m.Address))
                .With<InternalClusterAction.ISubscriptionMessage>(m => _publisher.Forward(m))
                .With<QuarantinedEvent>(m => Quarantined(new UniqueAddress(m.Address, m.Uid)))
                .With<ClusterUserAction.JoinTo>(
                    m => Log.Info("Trying to join [{0}] when already part of a cluster, ignoring", m.Address))
                .With<InternalClusterAction.JoinSeedNodes>(
                    m =>
                        Log.Info("Trying to join seed nodes [{0}] when already part of a cluster, ignoring",
                            m.SeedNodes.Select(a => a.ToString()).Aggregate((a, b) => a + ", " + b)));
        }

        protected override void OnReceive(object message)
        {
            Uninitialized(message);
        }

        protected override void Unhandled(object message)
        {
            message.Match()
                .With<InternalClusterAction.ITick>(m => { })
                .With<GossipEnvelope>(m => { })
                .With<GossipStatus>(m => { })
                .Default(m => base.Unhandled(m));
        }

        public void InitJoin()
        {
            Sender.Tell(new InternalClusterAction.InitJoinAck(_cluster.SelfAddress));
        }

        public void JoinSeedNodes(ImmutableList<Address> seedNodes)
        {
            if (seedNodes.Any())
            {
                StopSeedNodeProcess();
                if(seedNodes.SequenceEqual(ImmutableList.Create(_cluster.SelfAddress)))
                {
                    Self.Tell(new ClusterUserAction.JoinTo(_cluster.SelfAddress));
                    _seedNodeProcess = null;
                }
                else
                {
                    // use unique name of this actor, stopSeedNodeProcess doesn't wait for termination
                    _seedNodeProcessCounter += 1;
                    if (seedNodes.Head().Equals(_cluster.SelfAddress))
                    {
                        _seedNodeProcess = Context.ActorOf(Props.Create(() => new FirstSeedNodeProcess(seedNodes)),"firstSeedNodeProcess-" + _seedNodeProcessCounter);
                    }
                    else
                    {
                        _seedNodeProcess = Context.ActorOf(Props.Create(() => new JoinSeedNodeProcess(seedNodes)).WithDispatcher(_cluster.Settings.UseDispatcher), "joinSeedNodeProcess-" + _seedNodeProcessCounter);
                    }
                }
            }
        }
        
       // Try to join this cluster node with the node specified by `address`.
       // It's only allowed to join from an empty state, i.e. when not already a member.
       // A `Join(selfUniqueAddress)` command is sent to the node to join,
       // which will reply with a `Welcome` message.
        public void Join(Address address)
        {
            if (address.Protocol != _cluster.SelfAddress.Protocol)
            {
                Log.Warning("Trying to join member with wrong protocol, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.Protocol, address.Protocol);
            }
            else if (address.System != _cluster.SelfAddress.System)
            {
                Log.Warning(
                    "Trying to join member with wrong ActorSystem name, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.System, address.System);
            }
            else
            {
                //TODO: Akka exception?
                if(!_latestGossip.Members.IsEmpty) throw new InvalidOperationException("Join can only be done from an empty state");

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
        }

        //State transition to JOINING - new node joining.
        //Received `Join` message and replies with `Welcome` message, containing
        // current gossip state, including the new joining member.
        public void Joining(UniqueAddress node, ImmutableHashSet<string> roles)
        {
            if(node.Address.Protocol != _cluster.SelfAddress.Protocol)
            {
                Log.Warning("Member with wrong protocol tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.Protocol, node.Address.Protocol);
            }
            else if (node.Address.System != _cluster.SelfAddress.System)
            {
                Log.Warning(
                    "Member with wrong ActorSystem name tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.System, node.Address.System);
            }
            else
            {
                var localMembers = _latestGossip.Members;

                // check by address without uid to make sure that node with same host:port is not allowed
                // to join until previous node with that host:port has been removed from the cluster
                var alreadyMember = localMembers.Any(m => m.Address == node.Address);
                var isUnreachable = !_latestGossip.Overview.Reachability.IsReachable(node);

                if (alreadyMember) Log.Info("Existing member [{0}] is trying to join, ignoring", node);
                else if (isUnreachable) Log.Info("Unreachable member [{0}] is trying to join, ignoring", node);
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

                    Log.Info("Node [{0}] is JOINING, roles [{1}]", node.Address,
                        roles.Select(r => r.ToString()).Aggregate("", (a, b) => a + ", " + b));

                    if (!node.Equals(SelfUniqueAddress))
                    {
                        Sender.Tell(new InternalClusterAction.Welcome(SelfUniqueAddress, _latestGossip));
                    }

                    Publish(_latestGossip);
                }
            }
        }

        //Reply from Join request
        public void Welcome(Address joinWith, UniqueAddress from, Gossip gossip)
        {
            if(!_latestGossip.Members.IsEmpty) throw new InvalidOperationException("Welcome can only be done from an empty state");
            if (!joinWith.Equals(from.Address))
            {
                Log.Info("Ignoring welcome from [{0}] when trying to join with [{1}]", from.Address, joinWith);
            }
            else
            {
                Log.Info("Welcome from [{0}]", from.Address);
                _latestGossip = gossip.Seen(SelfUniqueAddress);
                Publish(_latestGossip);
                if(!from.Equals(SelfUniqueAddress))
                    GossipTo(from, Sender);
                BecomeInitialized();
            }
        }

        // State transition to LEAVING.
        // The node will eventually be removed by the leader, after hand-off in EXITING, and only after
        // removal a new node with same address can join the cluster through the normal joining procedure.
        public void Leaving(Address address)
        {
            // only try to update if the node is available (in the member ring)
            if(_latestGossip.Members.Any(m=> m.Address.Equals(address) && m.Status == MemberStatus.Up))
            {
                var newMembers = _latestGossip.Members.Select(m =>
                {
                    if (m.Address == address) return m.Copy(status: MemberStatus.Leaving);
                    return m;
                }).ToImmutableSortedSet(); // mark node as LEAVING
                var newGossip = _latestGossip.Copy(members: newMembers);

                UpdateLatestGossip(newGossip);

                Log.Info("Marked address [{0}] as Leaving]", address);
                Publish(_latestGossip);
            }
        }

        //This method is called when a member sees itself as Exiting or Down.
        public void Shutdown()
        {
            _cluster.Shutdown();
        }

        /**
        * State transition to DOWN.
        * Its status is set to DOWN. The node is also removed from the `seen` table.
        *
        * The node will eventually be removed by the leader, and only after removal a new node with same address can
        * join the cluster through the normal joining procedure.
        */
        public void Downing(Address address)
        {
            var localGossip = _latestGossip;
            var localMembers = localGossip.Members;
            var localOverview = localGossip.Overview;
            var localSeen = localOverview.Seen;
            var localReachability = localOverview.Reachability;

            //check if the node to DOWN is in the 'members' set
            var member = localMembers.FirstOrDefault(m => m.Address == address);
            if (member == null)
            {
                Log.Info("Ignoring down of unknown node [{0}]", address); 
            }
            else
            {
                var m = member.Copy(MemberStatus.Down);
                if (localReachability.IsReachable(m.UniqueAddress))
                    Log.Info("Marking node [{0}] as Down", m.Address);
                else
                    Log.Info("Marking unreachable node [{0}] as Down", MemberStatus.Down);

                // replace member (changed status)
                var newMembers = localMembers.Remove(m).Add(m);
                // remove nodes marked as DOWN from the 'seen' table
                var newSeen = localSeen.Remove(m.UniqueAddress);

                //update gossip overview
                var newOverview = localOverview.Copy(seen: newSeen);
                var newGossip = localGossip.Copy(members: newMembers, overview: newOverview); //update gossip
                UpdateLatestGossip(newGossip);

                Publish(_latestGossip);
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
                Log.Warn("Cluster Node [{0}] - Marking node as TERMINATED [{1}], due to quarantine",
                    Self, node.Address);
                Publish(_latestGossip);
                Downing(node.Address);
            }
        }

        public void ReceiveGossipStatus(GossipStatus status)
        {
            var from = status.From;
            if(!_latestGossip.Overview.Reachability.IsReachable(SelfUniqueAddress, from))
                Log.Info("Ignoring received gossip status from unreachable [{0}]", from);
            else if (_latestGossip.Members.All(m => !m.UniqueAddress.Equals(from)))
                Log.Debug("Cluster Node [{0}] - Ignoring received gossip status from unknown [{1}]",
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
                Log.Debug("Cluster Node [{0}] - Ignoring received gossip from [{1}] to protect against overload",
                    _cluster.SelfAddress, from);
                 return ReceiveGossipType.Ignored;
            }          
            if (!envelope.To.Equals(SelfUniqueAddress))
            {
                Log.Info("Ignoring received gossip intended for someone else, from [{0}] to [{1}]", 
                    from.Address, envelope.To);
                return ReceiveGossipType.Ignored;
            }
            if (!remoteGossip.Overview.Reachability.IsReachable(SelfUniqueAddress))
            {
                Log.Info("Ignoring received gossip with myself as unreachable, from [{0}]", from.Address);
                return ReceiveGossipType.Ignored;
            }
            if (!localGossip.Overview.Reachability.IsReachable(SelfUniqueAddress, from))
            {
                Log.Info("Ignoring received gossip from unreachable [{0}]", from);
                return ReceiveGossipType.Ignored;
            }
            if(localGossip.Members.All(m => !m.UniqueAddress.Equals(from)))
            {
                Log.Debug("Cluster Node [{0}] - Ignoring received gossip from unknown [{1}]", _cluster.SelfAddress, from);
                return ReceiveGossipType.Ignored;
            }
            if(remoteGossip.Members.All(m => !m.UniqueAddress.Equals(SelfUniqueAddress)))
            {
                Log.Debug("Ignoring received gossip that does not contain myself, from [{0}]", from);
                return ReceiveGossipType.Ignored;
            }

            var comparison = remoteGossip.Version.CompareTo(localGossip.Version);

            Gossip winningGossip = null;
            bool talkback = false;
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
                    //conflicting versions, merge
                    winningGossip = remoteGossip.Merge(localGossip);
                    talkback = true;
                    gossipType = ReceiveGossipType.Merge;
                    break;
            }

            _latestGossip = winningGossip.Seen(SelfUniqueAddress);

            // for all new joining nodes we remove them from the failure detector
            foreach (var node in _latestGossip.Members)
            {
                if (node.Status == MemberStatus.Joining && !localGossip.Members.Contains(node))
                    _cluster.FailureDetector.Remove(node.Address);
            }

            Log.Debug("Cluster Node [{0}] - Receiving gossip from [{1}]", _cluster.SelfAddress, from);

            if (comparison == VectorClock.Ordering.Concurrent)
            {
                Log.Debug(@"""Couldn't establish a causal relationship between ""remote"" gossip and ""local"" gossip - Remote[{0}] - Local[{1}] - merged them into [{2}]""",
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
            if(selfStatus == MemberStatus.Exiting || selfStatus == MemberStatus.Down)
                Shutdown();
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
                _cluster.Scheduler.ScheduleOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks/3), Self,
                    InternalClusterAction.GossipSpeedupTick.Instance);
                _cluster.Scheduler.ScheduleOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks * 2/3), Self,
                    InternalClusterAction.GossipSpeedupTick.Instance);
            }
        }

        public void GossipSpeedupTick()
        {
            if (IsGossipSpeedupNeeded()) SendGossip();
        }

        public bool IsGossipSpeedupNeeded()
        {
            return _latestGossip.Overview.Seen.Count < _latestGossip.Members.Count/2;
        }

        //Initiates a new round of gossip.
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
                    preferredGossipTarget = ImmutableList.Create<UniqueAddress>();
                }

                if (preferredGossipTarget.Any())
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
                        if(localGossip.SeenByNode(peer)) GossipStatusTo(peer);   
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
                if (size <= low) return _cluster.Settings.ReduceGossipDifferentViewProbability;

                // don't go lower than 1/10 of the configured GossipDifferentViewProbability
                var minP = _cluster.Settings.ReduceGossipDifferentViewProbability / 10;
                if (size >= high) return minP;
                else
                {
                    // linear reduction of the probability with increasing number of nodes
                    // from ReduceGossipDifferentViewProbability at ReduceGossipDifferentViewProbability nodes
                    // to ReduceGossipDifferentViewProbability / 10 at ReduceGossipDifferentViewProbability * 3 nodes
                    // i.e. default from 0.8 at 400 nodes, to 0.08 at 1600 nodes     
                    var k = (minP - _cluster.Settings.ReduceGossipDifferentViewProbability) / (high - low);
                    return _cluster.Settings.ReduceGossipDifferentViewProbability + (size - low) * k;
                }                
            }
        }

        /// <summary>
        /// Runs periodic leader actions, such as member status transitions, assigning partitions etc.
        /// </summary>
        public void LeaderActions()
        {
            if (_latestGossip.IsLeader(SelfUniqueAddress))
            {
                // only run the leader actions if we are the LEADER
                if (_latestGossip.Convergence)
                    LeaderActionsOnConvergence();
            }
        }

        /// Leader actions are as follows:
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
        public void LeaderActionsOnConvergence()
        {
            var localGossip = _latestGossip;
            var localMembers = localGossip.Members;
            var localOverview = localGossip.Overview;
            var localSeen = localOverview.Seen;

            //TODO (from JVM Akka) implement partion handoff and a check if it is completed - now just returns TRUE - e.g. has completed successfully
            var hasPartionHandoffCompletedSuccessfully = true;

            Func<bool> enoughMembers = () => localMembers.Count >= _cluster.Settings.MinNrOfMembers &&
                                             _cluster.Settings.MinNrOfMembersOfRole.All(
                                                 r => localMembers.Count(m => m.HasRole(r.Key)) >= r.Value);

            Func<Member, bool> isJoiningUp = m => m.Status == MemberStatus.Joining && enoughMembers();

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

                if (m.Status == MemberStatus.Leaving && hasPartionHandoffCompletedSuccessfully)
                {
                    // Move LEAVING => EXITING (once we have a convergence on LEAVING
                    // *and* if we have a successful partition handoff)
                    return m.Copy(MemberStatus.Exiting);
                }

                return null;
            }).Where(m => m != null).ToImmutableSortedSet();

            if (removedUnreachable.Any() || changedMembers.Any())
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
                var newGossip = localGossip.Copy(members: newMembers, overview: newOverview);

                UpdateLatestGossip(newGossip);

                // log status changes
                foreach (var m in changedMembers)
                    Log.Info("Leader is moving node [{0}] to [{1}]", m.Address, m.Status);

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
                    //TODO: Fire off a load of gossip messages in rapid succession?
                    for (var i = 0; i < NumberOfGossipsBeforeShutdownWhenLeaderExits; i++) SendGossip();
                    Shutdown();
                }
            }
        }

        public void ReapUnreachableMembers()
        {
            if (!IsSingletonCluster)
            {
                // only scrutinize if we are a non-singleton cluster

                var localGossip = _latestGossip;
                var localOverview = localGossip.Overview;
                var localMembers = localGossip.Members;

                var newlyDetectedUnreachableMembers =
                    localMembers.Where(member => !(member.UniqueAddress.Equals(SelfUniqueAddress) ||
                                                   localOverview.Reachability.Status(SelfUniqueAddress,
                                                       member.UniqueAddress) ==
                                                   Reachability.ReachabilityStatus.Unreachable ||
                                                   localOverview.Reachability.Status(SelfUniqueAddress,
                                                       member.UniqueAddress) ==
                                                   Reachability.ReachabilityStatus.Terminated ||
                                                   _cluster.FailureDetector.IsAvailable(member.Address)))
                                                   .ToImmutableSortedSet();

                var newlyDetectedReachableMembers =
                    localOverview.Reachability.AllUnreachableFrom(SelfUniqueAddress)
                        .Where(
                            node =>
                                !node.Equals(SelfUniqueAddress) && _cluster.FailureDetector.IsAvailable(node.Address))
                        .Select(localGossip.GetMember).ToImmutableHashSet();

                if (newlyDetectedUnreachableMembers.Any() || newlyDetectedReachableMembers.Any())
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

                        var partitioned =
                            newlyDetectedUnreachableMembers.Partition(m => m.Status == MemberStatus.Exiting);
                        var exiting = partitioned.Item1;
                        var nonExiting = partitioned.Item2;

                        if (nonExiting.Any())
                            Log.Warn("Cluster Node [{0}] - Marking node(s) as UNREACHABLE [{1}]",
                                _cluster.SelfAddress, nonExiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b));

                        if (exiting.Any())
                            Log.Warn("Marking exiting node(s) as UNREACHABLE [{0}]. This is expected and they will be removed.",
                                _cluster.SelfAddress, exiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b));

                        if (newlyDetectedReachableMembers.Any())
                            Log.Info("Marking node(s) as REACHABLE [{0}]", newlyDetectedReachableMembers.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b));

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

        //needed for tests
        public void SendGossipTo(Address address)
        {
            foreach (var m in _latestGossip.Members)
            {
                if (m.Address.Equals(address))
                    GossipTo(m.UniqueAddress);
            }
        }
        
        //Gossips latest gossip to a node.
        public void GossipTo(UniqueAddress node)
        {
            if(ValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipEnvelope(SelfUniqueAddress, node, _latestGossip));
        }

        public void GossipTo(UniqueAddress node, ActorRef destination)
        {
            if (ValidNodeForGossip(node))
                destination.Tell(new GossipEnvelope(SelfUniqueAddress, node, _latestGossip));
        }

        public void GossipStatusTo(UniqueAddress node)
        {
            if (ValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipStatus(SelfUniqueAddress, _latestGossip.Version));
        }

        public void GossipStatusTo(UniqueAddress node, ActorRef destination)
        {
            if(ValidNodeForGossip(node))
                destination.Tell(new GossipStatus(SelfUniqueAddress, _latestGossip.Version));
        }

        public bool ValidNodeForGossip(UniqueAddress node)
        {
            return !node.Equals(SelfUniqueAddress) && _latestGossip.HasMember(node) &&
                    _latestGossip.Overview.Reachability.IsReachable(node);
        }

        public void UpdateLatestGossip(Gossip newGossip)
        {
            // Updating the vclock version for the changes
            var versionedGossip = newGossip.Increment(_vclockNode);
            // Nobody else have seen this gossip but us
            var seenVersionedGossip = versionedGossip.OnlySeen(SelfUniqueAddress);
            // Update the state with the new gossip
            _latestGossip = seenVersionedGossip;
        }

        public void Publish(Gossip newGossip)
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(newGossip));
            if (_cluster.Settings.PublishStatsInterval == TimeSpan.MinValue) PublishInternalStats();
        }

        public void PublishInternalStats()
        {
            var vclockStats = new VectorClockStats(_latestGossip.Version.Versions.Count,
                _latestGossip.Members.Count(m => _latestGossip.SeenByNode(m.UniqueAddress)));

            _publisher.Tell(new ClusterEvent.CurrentInternalStats(_gossipStats, vclockStats));
        }

        readonly LoggingAdapter _log = Context.GetLogger();
        public LoggingAdapter Log { get { return _log; } }
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
    internal sealed class JoinSeedNodeProcess : UntypedActor, IActorLogging
    {
        readonly LoggingAdapter _log = Context.GetLogger();
        public LoggingAdapter Log { get { return _log; } }

        readonly ImmutableList<Address> _seeds;
        readonly Address _selfAddress;

        public JoinSeedNodeProcess(ImmutableList<Address> seeds)
        {
             _selfAddress = Cluster.Get(Context.System).SelfAddress;
            _seeds = seeds;
            if(!seeds.Any() || seeds.Head() == _selfAddress)
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
                foreach (
                    var path in
                        _seeds.Where(x => x != _selfAddress)
                            .Select(y => Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(y))))
                {
                   path.Tell(new InternalClusterAction.InitJoin()); 
                }
            }
            else if (message is InternalClusterAction.InitJoinAck)
            {
                //first InitJoinAck reply
                var initJoinAck = (InternalClusterAction.InitJoinAck) message;
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
            } else if(message is ReceiveTimeout) Context.Stop(Self);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Used only for the first seed node.
    /// Sends <see cref="InternalClusterAction.InitJoin"/> to all seed nodes except itself.
    /// If other seed nodes are not part of the clsuter yet they will reply with 
    /// <see cref="InternalClusterAction.InitJoinNack"/> or not respond at all and then the
    /// first seed node will join itself to initialize the new cluster. When the first seed 
    /// node is restarted, and some otehr seed node is part of the cluster it will reply with
    /// <see cref="InternalClusterAction.InitJoinAck"/> and then the first seed node will
    /// join that other seed node to join the existing cluster.
    /// </summary>
    internal sealed class FirstSeedNodeProcess : UntypedActor, IActorLogging
    {
        readonly LoggingAdapter _log = Context.GetLogger();
        public LoggingAdapter Log { get { return _log; } }

        private ImmutableList<Address> _remainingSeeds;
        readonly Address _selfAddress;
        readonly Cluster _cluster;
        readonly Deadline _timeout;
        private Task _retryTask;
        readonly CancellationTokenSource _retryTaskToken;

        public FirstSeedNodeProcess(ImmutableList<Address> seeds)
        {
            _cluster = Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;

            if (seeds.Count <= 1 || seeds.Head() != _selfAddress)
                throw new ArgumentException("Join seed node should not be done");

            _remainingSeeds = seeds.Remove(_selfAddress);
            _timeout = Deadline.Now + _cluster.Settings.SeedNodeTimeout;
            _retryTaskToken = new CancellationTokenSource();
            _retryTask = _cluster.Scheduler.Schedule(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Self,
                new InternalClusterAction.JoinSeenNode(), _retryTaskToken.Token);
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
                    foreach (
                        var seed in
                            _remainingSeeds.Select(
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
                var initJoinAck = (InternalClusterAction.InitJoinAck) message;
                Context.Parent.Tell(new ClusterUserAction.JoinTo(initJoinAck.Address));
                Context.Stop(Self);
            }
            else if (message is InternalClusterAction.InitJoinNack)
            {
                var initJoinNack = (InternalClusterAction.InitJoinNack) message;
                _remainingSeeds = _remainingSeeds.Remove(initJoinNack.Address);
                if (!_remainingSeeds.Any())
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
    /// The supplied callback will be run once when the current cluster member is <see cref="MemberStatus.Up"/>
    /// </summary>
    class OnMemberUpListener : ReceiveActor, IActorLogging
    {
        readonly Action _callback;
        readonly LoggingAdapter _log = Context.GetLogger();
        readonly Cluster _cluster;
        public LoggingAdapter Log { get { return _log; } }

        public OnMemberUpListener(Action callback)
        {
            _callback = callback;
            _cluster = Cluster.Get(Context.System);
            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                if(state.Members.Any(IsSelfUp))
                    Done();
            });

            Receive<ClusterEvent.MemberUp>(up =>
            {
                if (IsSelfUp(up.Member))
                    Done();
            });
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new []{ typeof(ClusterEvent.MemberUp) });
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        private void Done()
        {
            try
            {
                _callback.Invoke();
            }
            catch(Exception ex)
            {
                Log.Error(ex, "OnMemberUp callback failed with [{0}]", ex.Message);
            }
            finally
            {
                Context.Stop(Self);
            }
        }

        private bool IsSelfUp(Member m)
        {
            return m.UniqueAddress == _cluster.SelfUniqueAddress && m.Status == MemberStatus.Up;
        }
    }

    public class VectorClockStats
    {
        readonly int _versionSize;
        readonly int _seenLatest;

        public VectorClockStats(int versionSize = 0, int seenLatest = 0)
        {
            _versionSize = versionSize;
            _seenLatest = seenLatest;
        }

        public int VersionSize {get { return _versionSize;}}
        public int SeenLatest {get { return _seenLatest;}}

        public override bool Equals(object obj)
        {
            var other = obj as VectorClockStats;
            if (other == null) return false;
            return _versionSize == other._versionSize &&
                   _seenLatest == other._seenLatest;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_versionSize*397) ^ _seenLatest;
            }
        }
    }
}