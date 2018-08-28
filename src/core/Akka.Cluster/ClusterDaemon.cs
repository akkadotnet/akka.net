#region copyright

//-----------------------------------------------------------------------
// <copyright file="ClusterDaemon.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote;
using Akka.Util;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Cluster
{
    /// <summary>
    /// Base interface for all cluster messages. All ClusterMessage's are serializable.
    /// </summary>
    public interface IClusterMessage
    {
    }

    internal static class InternalClusterAction
    {
        /// <summary>
        /// Command to join the cluster. Sent when a node wants to join another node (the receiver).
        /// </summary>
        internal sealed class Join : IClusterMessage, IEquatable<Join>
        {
            /// <summary>
            /// The node that wants to join the cluster.
            /// </summary>
            public UniqueAddress Node { get; }

            public ImmutableHashSet<string> Roles { get; }

            public Join(UniqueAddress node, ImmutableHashSet<string> roles)
            {
                Node = node;
                Roles = roles;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is Join join && Equals(@join);

            public bool Equals(Join other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Node.Equals(other.Node) && Roles.SetEquals(other.Roles);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Node.GetHashCode();
                    foreach (var role in Roles)
                    {
                        hash = (hash * 397) ^ role.GetHashCode();
                    }

                    return hash;
                }
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                var roles = string.Join(",", Roles ?? ImmutableHashSet<string>.Empty);
                return $"{GetType()}: {Node} wants to join on Roles [{roles}]";
            }
        }

        /// <summary>
        /// Reply to Join
        /// </summary>
        internal sealed class Welcome : IClusterMessage, IEquatable<Welcome>
        {
            /// <summary>
            /// The sender node in the cluster, i.e. the node that received the <see cref="Join"/> command.
            /// </summary>
            public UniqueAddress From { get; }

            public Gossip Gossip { get; }

            public Welcome(UniqueAddress from, Gossip gossip)
            {
                From = from;
                Gossip = gossip;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is Welcome welcome && Equals(welcome);

            public bool Equals(Welcome other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return From.Equals(other.From) && Gossip.ToString().Equals(other.Gossip.ToString());
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (From.GetHashCode() * 397) ^ Gossip.GetHashCode();
                }
            }
        }

        /// <summary>
        /// Command to initiate the process to join the specified seed nodes.
        /// </summary>
        internal sealed class JoinSeedNodes : IEquatable<JoinSeedNodes>
        {
            /// <summary>
            /// Creates a new instance of the command.
            /// </summary>
            /// <param name="seedNodes">The list of seeds we wish to join.</param>
            public JoinSeedNodes(ImmutableList<Address> seedNodes)
            {
                SeedNodes = seedNodes;
            }

            /// <summary>
            /// The list of seeds we wish to join.
            /// </summary>
            public ImmutableList<Address> SeedNodes { get; }

            public bool Equals(JoinSeedNodes other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(SeedNodes, other.SeedNodes);
            }

            public override bool Equals(object obj) => obj is JoinSeedNodes nodes && Equals(nodes);

            public override int GetHashCode()
            {
                return (SeedNodes != null ? SeedNodes.GetHashCode() : 0);
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
        internal sealed class JoinSeedNode : IEquatable<JoinSeedNode>
        {
            public static JoinSeedNode Instance { get; } = new JoinSeedNode();

            private JoinSeedNode()
            {
            }

            public bool Equals(JoinSeedNode other) => other != null;
            public override bool Equals(object obj) => obj is JoinSeedNode node && Equals(node);
        }

        /// <inheritdoc cref="JoinSeedNode"/>
        internal class InitJoin : IClusterMessage, IDeadLetterSuppression, IEquatable<InitJoin>
        {
            public static InitJoin Instance { get; } = new InitJoin();

            private InitJoin()
            {
            }

            public bool Equals(InitJoin other) => other != null;
            public override bool Equals(object obj) => obj is InitJoin node && Equals(node);
        }

        /// <inheritdoc cref="JoinSeedNode"/>
        internal sealed class InitJoinAck : IClusterMessage, IDeadLetterSuppression, IEquatable<InitJoinAck>
        {
            public Address Address { get; }

            public InitJoinAck(Address address)
            {
                Address = address;
            }

            public bool Equals(InitJoinAck other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is InitJoinAck ack && Equals(ack);

            /// <inheritdoc/>
            public override int GetHashCode() => Address.GetHashCode();

            public override string ToString() => $"InitJoinAck(address: {Address})";
        }

        /// <inheritdoc cref="JoinSeedNode"/>
        internal sealed class InitJoinNack : IClusterMessage, IDeadLetterSuppression, IEquatable<InitJoinNack>
        {
            public Address Address { get; }

            public InitJoinNack(Address address)
            {
                Address = address;
            }

            public bool Equals(InitJoinNack other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is InitJoinNack ack && Equals(ack);

            /// <inheritdoc/>
            public override int GetHashCode() => Address.GetHashCode();

            public override string ToString() => $"InitJoinNack(address: {Address})";
        }

        /// <summary>
        /// Signals that a member is confirmed to be exiting the cluster
        /// </summary>
        internal sealed class ExitingConfirmed : IClusterMessage, IDeadLetterSuppression,
            IEquatable<ExitingConfirmed>
        {
            /// <summary>
            /// The member's address
            /// </summary>
            public UniqueAddress Address { get; }

            public ExitingConfirmed(UniqueAddress address)
            {
                Address = address;
            }

            public bool Equals(ExitingConfirmed other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Address.Equals(other.Address);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is ExitingConfirmed confirmed && Equals(confirmed);

            /// <inheritdoc/>
            public override int GetHashCode() => Address.GetHashCode();

            public override string ToString() => $"ExitingConfirmed(address: {Address})";
        }

        /// <summary>
        /// Used to signal that a self-exiting event has completed.
        /// </summary>
        internal sealed class ExitingCompleted
        {
            /// <summary>
            /// Singleton instance
            /// </summary>
            public static readonly ExitingCompleted Instance = new ExitingCompleted();

            private ExitingCompleted()
            {
            }
        }

        /// <summary>
        /// Marker interface for periodic tick messages
        /// </summary>
        internal enum Tick
        {
            /// <summary>
            /// Used to trigger the publication of gossip
            /// </summary>
            GossipTick,
            GossipSpeedupTick,
            ReapUnreachableTick,
            LeaderActionsTick,
            PublishStatsTick
        }

        internal sealed class SendGossipTo : IEquatable<SendGossipTo>
        {
            public Address Address { get; }

            public SendGossipTo(Address address)
            {
                Address = address;
            }

            public bool Equals(SendGossipTo other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            public override bool Equals(object obj) => obj is SendGossipTo to && Equals(to);
            public override int GetHashCode() => Address.GetHashCode();
            public override string ToString() => $"SendGossipTo(address: {Address})";
        }

        /// <summary>
        /// Gets a reference to the cluster core daemon.
        /// </summary>
        internal sealed class GetClusterCoreRef
        {
            private GetClusterCoreRef()
            {
            }

            /// <summary>
            /// The singleton instance
            /// </summary>
            public static GetClusterCoreRef Instance { get; } = new GetClusterCoreRef();
        }

        /// <summary>
        /// Command to <see cref="ClusterDaemon"/> to create a
        /// <see cref="OnMemberStatusChangedListener"/> that will be invoked
        /// when the current member is marked as up.
        /// </summary>
        public sealed class AddOnMemberUpListener : INoSerializationVerificationNeeded
        {
            public Action Callback { get; }

            public AddOnMemberUpListener(Action callback)
            {
                Callback = callback;
            }
        }

        /// <summary>
        /// Command to the <see cref="ClusterDaemon"/> to create a <see cref="OnMemberStatusChangedListener"/>
        /// that will be invoked when the current member is removed.
        /// </summary>
        public sealed class AddOnMemberRemovedListener : INoSerializationVerificationNeeded
        {
            public Action Callback { get; }

            public AddOnMemberRemovedListener(Action callback)
            {
                Callback = callback;
            }
        }

        /// <summary>
        /// All messages related to creating or removing <see cref="Cluster"/> event subscriptions
        /// </summary>
        public interface ISubscriptionMessage
        {
        }

        /// <summary>
        /// Subscribe an actor to new <see cref="Cluster"/> events.
        /// </summary>
        public sealed class Subscribe : ISubscriptionMessage, IEquatable<Subscribe>
        {
            /// <summary>
            /// Creates a new subscription
            /// </summary>
            /// <param name="subscriber">The actor being subscribed to events.</param>
            /// <param name="initialStateMode">The initial state of the subscription.</param>
            /// <param name="to">The range of event types to which we'll be subscribing.</param>
            public Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode,
                ImmutableHashSet<Type> to)
            {
                Subscriber = subscriber;
                InitialStateMode = initialStateMode;
                To = to;
            }

            /// <summary>
            /// The actor that is subscribed to cluster events.
            /// </summary>
            public IActorRef Subscriber { get; }

            /// <summary>
            /// The delivery mechanism for the initial cluster state.
            /// </summary>
            public ClusterEvent.SubscriptionInitialStateMode InitialStateMode { get; }

            /// <summary>
            /// The range of cluster events to which <see cref="Subscriber"/> is subscribed.
            /// </summary>
            public ImmutableHashSet<Type> To { get; }

            public bool Equals(Subscribe other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Subscriber, other.Subscriber) && InitialStateMode == other.InitialStateMode &&
                       Equals(To, other.To);
            }

            public override bool Equals(object obj) => obj is Subscribe subscribe && Equals(subscribe);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Subscriber != null ? Subscriber.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (int)InitialStateMode;
                    hashCode = (hashCode * 397) ^ (To != null ? To.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public override string ToString() =>
                $"Subscribe(subscriber: {Subscriber}, mode: {InitialStateMode}, to: [{string.Join(", ", To)}])";
        }

        /// <summary>
        /// Unsubscribe previously <see cref="Subscribe"/>d actor.
        /// </summary>
        public sealed class Unsubscribe : ISubscriptionMessage, IDeadLetterSuppression, IEquatable<Unsubscribe>
        {
            public IActorRef Subscriber { get; }
            public Type To { get; }

            public Unsubscribe(IActorRef subscriber, Type to)
            {
                To = to;
                Subscriber = subscriber;
            }

            public bool Equals(Unsubscribe other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Subscriber, other.Subscriber) && To == other.To;
            }

            public override bool Equals(object obj) => obj is Unsubscribe unsubscribe && Equals(unsubscribe);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Subscriber != null ? Subscriber.GetHashCode() : 0) * 397) ^
                           (To != null ? To.GetHashCode() : 0);
                }
            }

            public override string ToString() => $"Unsubscribe(subscriber: {Subscriber}, from: {To})";
        }

        public sealed class SendCurrentClusterState : ISubscriptionMessage, IEquatable<SendCurrentClusterState>
        {
            /// <summary>
            /// <see cref="Akka.Cluster.ClusterEvent.CurrentClusterState"/> will be sent to the receiver.
            /// </summary>
            public IActorRef Receiver { get; }

            public SendCurrentClusterState(IActorRef receiver)
            {
                Receiver = receiver;
            }

            public bool Equals(SendCurrentClusterState other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Receiver, other.Receiver);
            }

            public override bool Equals(object obj) => obj is SendCurrentClusterState state && Equals(state);
            public override int GetHashCode() => Receiver.GetHashCode();
            public override string ToString() => $"SendCurrentClusterState(receiver: {Receiver})";
        }

        interface IPublishMessage
        {
        }

        internal sealed class PublishChanges : IPublishMessage, IEquatable<PublishChanges>
        {
            public MembershipState State { get; }

            internal PublishChanges(MembershipState state)
            {
                State = state;
            }

            public bool Equals(PublishChanges other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(State, other.State);
            }

            public override bool Equals(object obj) => obj is PublishChanges changes && Equals(changes);
            public override int GetHashCode() => State.GetHashCode();
            public override string ToString() => $"PublishChanges(state: {State})";
        }

        internal sealed class PublishEvent : IPublishMessage, IEquatable<PublishEvent>
        {
            public ClusterEvent.IClusterDomainEvent Event { get; }

            internal PublishEvent(ClusterEvent.IClusterDomainEvent @event)
            {
                Event = @event;
            }

            public bool Equals(PublishEvent other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Event, other.Event);
            }

            public override bool Equals(object obj) => obj is PublishEvent @event && Equals(@event);
            public override int GetHashCode() => Event.GetHashCode();
            public override string ToString() => $"PublishEvent(event: {Event})";
        }
    }

    /// <summary>
    /// Cluster commands sent by the USER via <see cref="Cluster"/> extension.
    /// </summary>
    internal static class ClusterUserAction
    {
        /// <summary>
        /// Command to initiate join another node (represented by <see cref="Address"/>).
        /// Join will be sent to the other node.
        /// </summary>
        internal sealed class JoinTo : IEquatable<JoinTo>
        {
            public Address Address { get; }

            public JoinTo(Address address)
            {
                Address = address;
            }

            public bool Equals(JoinTo other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            public override bool Equals(object obj) => obj is JoinTo joinTo && Equals(joinTo);
            public override int GetHashCode() => Address.GetHashCode();
            public override string ToString() => $"JoinTo(address: {Address})";
        }

        /// <summary>
        /// Command to leave the cluster.
        /// </summary>
        internal sealed class Leave : IClusterMessage, IEquatable<Leave>
        {
            public Address Address { get; }

            public Leave(Address address)
            {
                Address = address;
            }

            public bool Equals(Leave other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            public override bool Equals(object obj) => obj is Leave leave && Equals(leave);
            public override int GetHashCode() => Address.GetHashCode();
            public override string ToString() => $"Leave(address: {Address})";
        }

        /// <summary>
        /// Command to mark node as temporary down.
        /// </summary>
        internal sealed class Down : IClusterMessage, IEquatable<Down>
        {
            public Address Address { get; }

            public Down(Address address)
            {
                Address = address;
            }

            public bool Equals(Down other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Address, other.Address);
            }

            public override bool Equals(object obj) => obj is Down down && Equals(down);
            public override int GetHashCode() => Address.GetHashCode();
            public override string ToString() => $"Down(address: {Address})";
        }
    }

    /// <summary>
    /// Supervisor managing the different Cluster daemons.
    /// </summary>
    internal sealed class ClusterDaemon : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        // Important - don't use Cluster(context.system) in constructor because that would
        // cause deadlock. The Cluster extension is currently being created and is waiting
        // for response from GetClusterCoreRef in its constructor.
        // Child actors are therefore created when GetClusterCoreRef is received

        private IActorRef _coreSupervisor = null;

        private readonly ClusterSettings _settings;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);
        private readonly TaskCompletionSource<Done> _clusterShutdown = new TaskCompletionSource<Done>();

        /// <summary>
        /// Creates a new instance of the ClusterDaemon
        /// </summary>
        /// <param name="settings">The settings that will be used for the <see cref="Cluster"/>.</param>
        public ClusterDaemon(ClusterSettings settings)
        {
            // Important - don't use Cluster(context.system) in constructor because that would
            // cause deadlock. The Cluster extension is currently being created and is waiting
            // for response from GetClusterCoreRef in its constructor.
            // Child actors are therefore created when GetClusterCoreRef is received
            _coreSupervisor = null;
            _settings = settings;

            var sys = Context.System;
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterLeave, "leave", async cancel =>
            {
                var cluster = Cluster.Get(sys);
                if (!(cluster.IsTerminated || cluster.SelfMember.Status == MemberStatus.Down))
                {
                    await self.Ask<Done>(CoordinatedShutdownLeave.LeaveReq.Instance, cancel);
                }
            });

            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterShutdown, "wait-shutdown",
                (cancelationToken) => _clusterShutdown.Task);
        }

        protected override void PostStop()
        {
            _clusterShutdown.TrySetResult(Done.Instance);

            if (_settings.RunCoordinatedShutdownWhenDown)
            {
                // if it was stopped due to leaving CoordinatedShutdown was started earlier
                _coordShutdown.Run(CoordinatedShutdown.Reason.ClusterDowning);
            }

            base.PostStop();
        }

        private void CreateChildren()
        {
            _coreSupervisor =
                Context.ActorOf(Props.Create<ClusterCoreSupervisor>().WithDispatcher(Context.Props.Dispatcher),
                    "core");
            Context.ActorOf(Props.Create<ClusterHeartbeatReceiver>().WithDispatcher(Context.Props.Dispatcher),
                "heartbeatReceiver");
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InternalClusterAction.GetClusterCoreRef msg:
                    if (_coreSupervisor == null)
                        CreateChildren();
                    _coreSupervisor.Forward(msg);
                    return true;
                case InternalClusterAction.AddOnMemberUpListener up:
                    Context.ActorOf(Props
                        .Create(() => new OnMemberStatusChangedListener(up.Callback, MemberStatus.Up))
                        .WithDeploy(Deploy.Local));
                    return true;
                case InternalClusterAction.AddOnMemberRemovedListener removed:
                    Context.ActorOf(Props
                        .Create(() => new OnMemberStatusChangedListener(removed.Callback, MemberStatus.Removed))
                        .WithDeploy(Deploy.Local));
                    return true;
                case CoordinatedShutdownLeave.LeaveReq leaveReq:
                    var aref = Context.ActorOf(Props.Create(() => new CoordinatedShutdownLeave())
                        .WithDispatcher(Context.Props.Dispatcher));
                    // forward the Ask request so the shutdown task gets completed
                    aref.Forward(leaveReq);
                    return true;
                default: return true;
            }
        }
    }

    /// <summary>
    /// ClusterCoreDaemon and ClusterDomainEventPublisher can't be restarted because the state
    /// would be obsolete. Shutdown the member if any those actors crashed.
    /// </summary>
    internal sealed class ClusterCoreSupervisor : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        // Important - don't use Cluster(context.system) in constructor because that would
        // cause deadlock. The Cluster extension is currently being created and is waiting
        // for response from GetClusterCoreRef in its constructor.
        // Child actors are therefore created when GetClusterCoreRef is received

        private IActorRef _coreDaemon = null;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private void CreateChildren()
        {
            var publisher =
                Context.ActorOf(
                    Props.Create<ClusterDomainEventPublisher>().WithDispatcher(Context.Props.Dispatcher),
                    "publisher");
            _coreDaemon =
                Context.ActorOf(
                    Props.Create(() => new ClusterCoreDaemon(publisher)).WithDispatcher(Context.Props.Dispatcher),
                    "daemon");
        }

        protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(e =>
        {
            //TODO: JVM version matches NonFatal. Can / should we do something similar? 
            _log.Error(e, "Cluster node [{0}] crashed, [{1}] - shutting down...",
                Cluster.Get(Context.System).SelfAddress, e);
            Self.Tell(PoisonPill.Instance);
            return Directive.Stop;
        });

        protected override void PostStop() => Cluster.Get(Context.System).Shutdown();

        protected override bool Receive(object message)
        {
            if (message is InternalClusterAction.GetClusterCoreRef)
            {
                if (_coreDaemon == null)
                    CreateChildren();
                Sender.Tell(_coreDaemon);
                return true;
            }
            else return false;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Actor used to power the guts of the Akka.Cluster membership and gossip protocols.
    /// </summary>
    internal class ClusterCoreDaemon : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private const int NumberOfGossipsBeforeShutdownWhenLeaderExits = 5;
        private const int MaxGossipsBeforeShuttingDownMyself = 5;

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly string _selfDataCenter;

        /// <summary>
        /// The current self-unique address.
        /// </summary>
        protected UniqueAddress SelfUniqueAddress => _cluster.SelfUniqueAddress;

        protected Address SelfAddress => _cluster.SelfAddress;
        protected Gossip LatestGossip => _membershipState.LatestGossip;

        private readonly VectorClock.Node _vclockNode;
        private readonly GossipTargetSelector _gossipTargetSelector;

        // note that self is not initially member,
        // and the Gossip is not versioned for this 'Node' yet
        private MembershipState _membershipState;

        private readonly bool _statsEnabled;
        private GossipStats _gossipStats = new GossipStats();
        private ImmutableList<Address> _seedNodes;
        private IActorRef _seedNodeProcess = null;
        private int _seedNodeProcessCounter = 0; //for unique names
        private Deadline _joinSeedNodesDeadline = null;
        private int _leaderActionCounter = 0;

        private bool _exitingTasksInProgress = false;
        private readonly TaskCompletionSource<Done> _selfExiting = new TaskCompletionSource<Done>();
        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);
        private ImmutableHashSet<UniqueAddress> _exitingConfirmed = ImmutableHashSet<UniqueAddress>.Empty;
        private readonly IActorRef _publisher;

        private readonly ICancelable _gossipTaskCancellable;
        private readonly ICancelable _failureDetectorReaperTaskCancellable;
        private readonly ICancelable _leaderActionsTaskCancellable;
        private readonly ICancelable _publishStatsTaskTaskCancellable;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// Creates a new cluster core daemon instance.
        /// </summary>
        /// <param name="publisher">A reference to the <see cref="ClusterDomainEventPublisher"/>.</param>
        public ClusterCoreDaemon(IActorRef publisher)
        {
            var settings = _cluster.Settings;
            var scheduler = _cluster.Scheduler;

            _selfDataCenter = _cluster.SelfDataCenter;
            _publisher = publisher;
            _vclockNode = VectorClock.Node.Create(Gossip.VectorClockName(SelfUniqueAddress));
            _gossipTargetSelector = new GossipTargetSelector(
                settings.ReduceGossipDifferentViewProbability,
                settings.MultiDataCenter.CrossDcConnections);

            _membershipState = new MembershipState(
                latestGossip: Gossip.Empty,
                selfUniqueAddress: _cluster.SelfUniqueAddress,
                selfDataCenter: settings.SelfDataCenter,
                crossDataCenterConnections: settings.MultiDataCenter.CrossDcConnections);

            _statsEnabled = settings.PublishStatsInterval.HasValue
                            && settings.PublishStatsInterval >= TimeSpan.Zero
                            && settings.PublishStatsInterval != TimeSpan.MaxValue;

            _seedNodes = _cluster.Settings.SeedNodes;

            // start periodic gossip to random nodes in cluster
            _gossipTaskCancellable = scheduler.ScheduleTellRepeatedlyCancelable(
                settings.PeriodicTasksInitialDelay.Max(settings.GossipInterval),
                settings.GossipInterval,
                Self,
                InternalClusterAction.Tick.GossipTick,
                Self);

            // start periodic cluster failure detector reaping (moving nodes condemned by the failure detector to unreachable list)
            _failureDetectorReaperTaskCancellable =
                scheduler.ScheduleTellRepeatedlyCancelable(
                    settings.PeriodicTasksInitialDelay.Max(settings.UnreachableNodesReaperInterval),
                    settings.UnreachableNodesReaperInterval,
                    Self,
                    InternalClusterAction.Tick.ReapUnreachableTick,
                    Self);

            // start periodic leader action management (only applies for the current leader)
            _leaderActionsTaskCancellable =
                scheduler.ScheduleTellRepeatedlyCancelable(
                    settings.PeriodicTasksInitialDelay.Max(settings.LeaderActionsInterval),
                    settings.LeaderActionsInterval,
                    Self,
                    InternalClusterAction.Tick.LeaderActionsTick,
                    Self);

            // start periodic publish of current stats
            if (settings.PublishStatsInterval != null && settings.PublishStatsInterval > TimeSpan.Zero &&
                settings.PublishStatsInterval != TimeSpan.MaxValue)
            {
                _publishStatsTaskTaskCancellable =
                    scheduler.ScheduleTellRepeatedlyCancelable(
                        settings.PeriodicTasksInitialDelay.Max(settings.PublishStatsInterval.Value),
                        settings.PublishStatsInterval.Value,
                        Self,
                        InternalClusterAction.Tick.PublishStatsTick,
                        Self);
            }

            // register shutdown tasks
            AddCoordinatedLeave();
        }

        private void AddCoordinatedLeave()
        {
            var sys = Context.System;
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "wait-exiting",
                () => _selfExiting.Task);
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExitingDone, "exiting-completed", async cancel =>
            {
                if (!Cluster.Get(sys).IsTerminated)
                {
                    await self.Ask(InternalClusterAction.ExitingCompleted.Instance, cancel);
                }
            });
        }

        private ActorSelection ClusterCore(Address address) =>
            Context.ActorSelection(new RootActorPath(address) / "system" / "cluster" / "core" / "daemon");

        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            Context.System.EventStream.Subscribe(Self, typeof(QuarantinedEvent));

            if (_cluster.DowningProvider.DowningActorProps != null)
            {
                var props = _cluster.DowningProvider.DowningActorProps;
                var propsWithDispatcher = props.Dispatcher == Deploy.NoDispatcherGiven
                    ? props.WithDispatcher(Context.Props.Dispatcher)
                    : props;

                Context.ActorOf(propsWithDispatcher, "downingProvider");
            }

            if (_seedNodes.IsEmpty)
                _cluster.LogInfo("No seed-nodes configured, manual cluster join required");
            else
                Self.Tell(new InternalClusterAction.JoinSeedNodes(_seedNodes));
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            Context.System.EventStream.Unsubscribe(Self);
            _gossipTaskCancellable.Cancel();
            _failureDetectorReaperTaskCancellable.Cancel();
            _leaderActionsTaskCancellable.Cancel();
            _publishStatsTaskTaskCancellable?.Cancel();
            _selfExiting.TrySetResult(Done.Instance);
            base.PostStop();
        }

        private bool Uninitialized(object message)
        {
            switch (message)
            {
                case InternalClusterAction.InitJoin _:
                    _cluster.LogInfo("Received InitJoin message from {0}, but this node is not initialized yet",
                        Sender);
                    Sender.Tell(new InternalClusterAction.InitJoinNack(SelfAddress));
                    return true;
                case ClusterUserAction.JoinTo joinTo:
                    Join(joinTo.Address);
                    return true;
                case InternalClusterAction.JoinSeedNodes joinSeedNodes:
                    ResetJoinSeedNodesDeadline();
                    JoinSeedNodes(joinSeedNodes.SeedNodes);
                    return true;
                case InternalClusterAction.ISubscriptionMessage msg:
                    _publisher.Forward(msg);
                    return true;
                case InternalClusterAction.Tick tick:
                    if (_joinSeedNodesDeadline?.IsOverdue ?? false)
                        JoinSeedNodesWasUnsuccessful();
                    return true;
                default:
                    return ReceiveExitingCompleted(message);
            }
        }

        private Receive TryingToJoin(Address joinWith, Deadline deadline) => (message) =>
        {
            switch (message)
            {
                case InternalClusterAction.Welcome welcome:
                    Welcome(joinWith, welcome.From, welcome.Gossip);
                    return true;

                case InternalClusterAction.InitJoin _:
                    _cluster.LogInfo("Received InitJoin message from {0}, but this node is not a member yet",
                        Sender);
                    Sender.Tell(new InternalClusterAction.InitJoinNack(SelfAddress));
                    return true;

                case ClusterUserAction.JoinTo joinTo:
                    BecomeUninitialized();
                    Join(joinTo.Address);
                    return true;

                case InternalClusterAction.JoinSeedNodes join:
                    ResetJoinSeedNodesDeadline();
                    BecomeUninitialized();
                    JoinSeedNodes(join.SeedNodes);
                    return true;

                case InternalClusterAction.ISubscriptionMessage msg:
                    _publisher.Forward(msg);
                    return true;

                case InternalClusterAction.Tick tick:
                    if (_joinSeedNodesDeadline?.IsOverdue ?? false)
                        JoinSeedNodesWasUnsuccessful();
                    else if (deadline?.IsOverdue ?? false)
                    {
                        // join attempt failed, retry
                        BecomeUninitialized();
                        if (!_seedNodes.IsEmpty) JoinSeedNodes(_seedNodes);
                        else Join(joinWith);
                    }

                    return true;

                default:
                    return ReceiveExitingCompleted(message);
            }
        };

        private void ResetJoinSeedNodesDeadline()
        {
            var timeout = _cluster.Settings.ShutdownAfterUnsuccessfulJoinSeedNodes;
            if (timeout.HasValue)
                _joinSeedNodesDeadline = Deadline.Now + timeout.Value;
            else
                _joinSeedNodesDeadline = null;
        }

        private void JoinSeedNodesWasUnsuccessful()
        {
            if (_log.IsWarningEnabled)
            {
                _log.Warning(
                    "Joining of seed-nodes [{0}] was unsuccessful after configured shutdown-after-unsuccessful-join-seed-nodes [{1}]. Running CoordinatedShutdown.",
                    string.Join(", ", _seedNodes), _cluster.Settings.ShutdownAfterUnsuccessfulJoinSeedNodes);
            }

            _joinSeedNodesDeadline = null;
            CoordinatedShutdown.Get(Context.System).Run(CoordinatedShutdown.Reason.ClusterDowning);
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
            var internalHeartbeatSender = Props.Create<ClusterHeartbeatSender>()
                .WithDispatcher(_cluster.Settings.UseDispatcher);
            Context.ActorOf(internalHeartbeatSender, "heartbeatSender");

            var externalHeartbeatSender = Props.Create<CrossDcHeartbeatSender>()
                .WithDispatcher(_cluster.Settings.UseDispatcher);
            Context.ActorOf(externalHeartbeatSender, "crossDcHeartbeatSender");

            // make sure that join process is stopped
            StopSeedNodeProcess();
            _joinSeedNodesDeadline = null;
            Context.Become(Initialized);
        }

        private bool Initialized(object message)
        {
            switch (message)
            {
                case GossipEnvelope envelope:
                    ReceiveGossip(envelope);
                    return true;
                case GossipStatus status:
                    ReceiveGossipStatus(status);
                    return true;
                case InternalClusterAction.Tick tick:
                    switch (tick)
                    {
                        case InternalClusterAction.Tick.GossipTick:
                            GossipTick();
                            break;
                        case InternalClusterAction.Tick.GossipSpeedupTick:
                            GossipSpeedupTick();
                            break;
                        case InternalClusterAction.Tick.ReapUnreachableTick:
                            ReapUnreachableMembers();
                            break;
                        case InternalClusterAction.Tick.LeaderActionsTick:
                            LeaderActions();
                            break;
                        case InternalClusterAction.Tick.PublishStatsTick:
                            PublishInternalStats();
                            break;
                    }

                    return true;
                case InternalClusterAction.InitJoin _:
                    _cluster.LogInfo("Received InitJoin message from [{0}] to [{1}]", Sender, SelfAddress);
                    InitJoin();
                    return true;
                case InternalClusterAction.Join join:
                    Joining(join.Node, join.Roles);
                    return true;
                case ClusterUserAction.Down down:
                    Downing(down.Address);
                    return true;
                case ClusterUserAction.Leave leave:
                    Leaving(leave.Address);
                    return true;
                case InternalClusterAction.SendGossipTo sendGossipTo:
                    SendGossipTo(sendGossipTo.Address);
                    return true;
                case InternalClusterAction.ISubscriptionMessage msg:
                    _publisher.Forward(msg);
                    return true;
                case QuarantinedEvent quarantined:
                    Quarantined(new UniqueAddress(quarantined.Address, quarantined.Uid));
                    return true;
                case ClusterUserAction.JoinTo joinTo:
                    _cluster.LogInfo("Trying to join [{0}] when already part of a cluster, ignoring", SelfAddress);
                    return true;
                case InternalClusterAction.JoinSeedNodes joinSeedNodes:
                    _cluster.LogInfo("Trying to join seed nodes [{0}] when already part of a cluster, ignoring",
                        string.Join(", ", joinSeedNodes.SeedNodes));
                    return true;
                case InternalClusterAction.ExitingConfirmed confirmed:
                    ReceiveExitingConfirmed(confirmed.Address);
                    return true;
                default: return ReceiveExitingCompleted(message);
            }
        }

        private bool ReceiveExitingCompleted(object message)
        {
            if (message is InternalClusterAction.ExitingCompleted)
            {
                ExitingCompleted();
                // complete the Ask
                Sender.Tell(Done.Instance);
                return true;
            }

            return false;
        }

        protected override bool Receive(object message) => Uninitialized(message);

        /// <inheritdoc cref="ActorBase.Unhandled"/>
        protected override void Unhandled(object message)
        {
            if (!(message is InternalClusterAction.Tick
                  || message is GossipEnvelope
                  || message is GossipStatus
                  || message is InternalClusterAction.ExitingConfirmed))
                base.Unhandled(message);
        }

        /// <summary>
        /// Begins the joining process.
        /// </summary>
        private void InitJoin()
        {
            var selfStatus = LatestGossip.GetMember(SelfUniqueAddress).Status;
            if (MembershipState.IsRemoveUnreachableWithMemberStatus(selfStatus))
            {
                // prevents a Down and Exiting node from being used for joining
                _cluster.LogInfo("Sending InitJoinNack message from node {0} to {1}", SelfAddress, Sender);
                Sender.Tell(new InternalClusterAction.InitJoinNack(_cluster.SelfAddress));
            }
            else
            {
                _cluster.LogInfo("Sending InitJoinAck message from node {0} to {1}", SelfAddress, Sender);
                Sender.Tell(new InternalClusterAction.InitJoinAck(_cluster.SelfAddress));
            }
        }

        /// <summary>
        /// Attempts to join this node or one or more seed nodes.
        /// </summary>
        /// <param name="newSeedNodes">The list of seed node we're attempting to join.</param>
        private void JoinSeedNodes(ImmutableList<Address> newSeedNodes)
        {
            if (!newSeedNodes.IsEmpty)
            {
                StopSeedNodeProcess();
                _seedNodes = newSeedNodes; // keep them for retry
                if (newSeedNodes.SequenceEqual(ImmutableList.Create(_cluster.SelfAddress))
                ) // self-join for a singleton cluster
                {
                    Self.Tell(new ClusterUserAction.JoinTo(_cluster.SelfAddress));
                    _seedNodeProcess = null;
                }
                else
                {
                    // use unique name of this actor, stopSeedNodeProcess doesn't wait for termination
                    _seedNodeProcessCounter++;
                    if (newSeedNodes.First().Equals(_cluster.SelfAddress))
                    {
                        _seedNodeProcess = Context.ActorOf(
                            Props.Create(() => new FirstSeedNodeProcess(newSeedNodes))
                                .WithDispatcher(_cluster.Settings.UseDispatcher),
                            "firstSeedNodeProcess-" + _seedNodeProcessCounter);
                    }
                    else
                    {
                        _seedNodeProcess = Context.ActorOf(
                            Props.Create(() => new JoinSeedNodeProcess(newSeedNodes))
                                .WithDispatcher(_cluster.Settings.UseDispatcher),
                            "joinSeedNodeProcess-" + _seedNodeProcessCounter);
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
        /// <param name="address">The address of the node we're going to join.</param>
        /// <exception cref="InvalidOperationException">Join can only be done from an empty state</exception>
        private void Join(Address address)
        {
            if (address.Protocol != _cluster.SelfAddress.Protocol)
            {
                _log.Warning(
                    "Trying to join member with wrong protocol, but was ignored, expected [{0}] but was [{1}]",
                    SelfAddress.Protocol, address.Protocol);
            }
            else if (address.System != _cluster.SelfAddress.System)
            {
                _log.Warning(
                    "Trying to join member with wrong ActorSystem name, but was ignored, expected [{0}] but was [{1}]",
                    SelfAddress.System, address.System);
            }
            else
            {
                //TODO: Akka exception?
                if (!LatestGossip.Members.IsEmpty)
                    throw new InvalidOperationException("Join can only be done from an empty state");

                // to support manual join when joining to seed nodes is stuck (no seed nodes available)
                StopSeedNodeProcess();

                if (address.Equals(SelfAddress))
                {
                    BecomeInitialized();
                    Joining(SelfUniqueAddress, _cluster.SelfRoles);
                }
                else
                {
                    var joinDeadline = _cluster.Settings.RetryUnsuccessfulJoinAfter == null
                        ? default(Deadline)
                        : Deadline.Now + _cluster.Settings.RetryUnsuccessfulJoinAfter;

                    Context.Become(TryingToJoin(address, joinDeadline));
                    ClusterCore(address)
                        .Tell(new InternalClusterAction.Join(SelfUniqueAddress, _cluster.SelfRoles));
                }
            }
        }

        /// <summary>
        /// Stops the seed node process after the cluster has started.
        /// </summary>
        private void StopSeedNodeProcess()
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
        /// <param name="joiningNode">TBD</param>
        /// <param name="roles">TBD</param>
        private void Joining(UniqueAddress joiningNode, ImmutableHashSet<string> roles)
        {
            var selfStatus = LatestGossip.GetMember(SelfUniqueAddress).Status;
            if (joiningNode.Address.Protocol != _cluster.SelfAddress.Protocol)
            {
                _log.Warning(
                    "Member with wrong protocol tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.Protocol, joiningNode.Address.Protocol);
            }
            else if (joiningNode.Address.System != _cluster.SelfAddress.System)
            {
                _log.Warning(
                    "Member with wrong ActorSystem name tried to join, but was ignored, expected [{0}] but was [{1}]",
                    _cluster.SelfAddress.System, joiningNode.Address.System);
            }
            else if (MembershipState.IsRemoveUnreachableWithMemberStatus(selfStatus))
            {
                _log.Info("Trying to join [{0}] to [{1}] member, ignoring. Use a member that is Up instead.", joiningNode,
                    selfStatus);
            }
            else
            {
                var localMembers = LatestGossip.Members;

                // check by address without uid to make sure that node with same host:port is not allowed
                // to join until previous node with that host:port has been removed from the cluster
                var localMember = localMembers.FirstOrDefault(m => m.Address.Equals(joiningNode.Address));
                if (localMember != null && localMember.UniqueAddress.Equals(joiningNode))
                {
                    // node retried join attempt, probably due to lost Welcome message
                    _cluster.LogInfo("Existing member [{0}] is joining again.", joiningNode);
                    if (!joiningNode.Equals(SelfUniqueAddress))
                    {
                        Sender.Tell(new InternalClusterAction.Welcome(SelfUniqueAddress, LatestGossip));
                    }
                }
                else if (localMember != null)
                {
                    // node restarted, same host:port as existing member, but with different uid
                    // safe to down and later remove existing member
                    // new node will retry join
                    _cluster.LogInfo(
                        "New incarnation of existing member [{0}] is trying to join. Existing will be removed from the cluster and then new member will be allowed to join.",
                        joiningNode);
                    if (localMember.Status != MemberStatus.Down)
                    {
                        // we can confirm it as terminated/unreachable immediately
                        var newReachability =
                            LatestGossip.Overview.Reachability.Terminated(_cluster.SelfUniqueAddress,
                                localMember.UniqueAddress);
                        var newOverview = LatestGossip.Overview.Copy(reachability: newReachability);
                        var newGossip = LatestGossip.Copy(overview: newOverview);
                        UpdateLatestGossip(newGossip);

                        Downing(localMember.Address);
                    }
                }
                else
                {
                    // remove the node from the failure detector
                    _cluster.FailureDetector.Remove(joiningNode.Address);
                    _cluster.CrossDcFailureDetector.Remove(joiningNode.Address);

                    // add joining node as Joining
                    // add self in case someone else joins before self has joined (Set discards duplicates)
                    var newMembers = localMembers
                        .Add(Member.Create(joiningNode, roles))
                        .Add(Member.Create(_cluster.SelfUniqueAddress, _cluster.SelfRoles));
                    var newGossip = LatestGossip.Copy(members: newMembers);

                    UpdateLatestGossip(newGossip);

                    _cluster.LogInfo("Node [{0}] is JOINING, roles [{1}]", joiningNode.Address, string.Join(",", roles));

                    if (joiningNode.Equals(SelfUniqueAddress))
                    {
                        if (localMembers.IsEmpty)
                        {
                            // important for deterministic oldest when bootstrapping
                            LeaderActions();
                        }
                    }
                    else
                    {
                        Sender.Tell(new InternalClusterAction.Welcome(SelfUniqueAddress, LatestGossip));
                    }

                    PublishMembershipState(); // joining
                }
            }
        }

        /// <summary>
        /// Reply from Join request
        /// </summary>
        /// <exception cref="InvalidOperationException">Welcome can only be done from an empty state</exception>
        private void Welcome(Address joinWith, UniqueAddress from, Gossip gossip)
        {
            if (!LatestGossip.Members.IsEmpty)
                throw new InvalidOperationException("Welcome can only be done from an empty state");

            if (!joinWith.Equals(from.Address))
                _cluster.LogInfo("Ignoring welcome from [{0}] when trying to join with [{1}]", from.Address,
                    joinWith);
            else
            {
                _membershipState = _membershipState.Copy(latestGossip: gossip).Seen();

                _cluster.LogInfo("Welcome from [{0}]", from.Address);
                AssertLatestGossip();
                PublishMembershipState(); // welcome
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
        /// <param name="address">The address of the node who is leaving the cluster.</param>
        private void Leaving(Address address)
        {
            // only try to update if the node is available (in the member ring)
            if (LatestGossip.Members.Any(m =>
                m.Address.Equals(address) && (m.Status == MemberStatus.Up || m.Status == MemberStatus.Joining ||
                                              m.Status == MemberStatus.WeaklyUp)))
            {
                // mark node as LEAVING
                var newMembers = LatestGossip.Members
                    .Select(m => m.Address == address ? m.Copy(status: MemberStatus.Leaving) : m)
                    .ToImmutableSortedSet(); // mark node as LEAVING
                var newGossip = LatestGossip.Copy(members: newMembers);

                UpdateLatestGossip(newGossip);

                _cluster.LogInfo("Marked address [{0}] as [{1}]", address, MemberStatus.Leaving);
                PublishMembershipState(); // leaving
                // immediate gossip to speed up the leaving process
                SendGossip();
            }
        }

        private void ExitingCompleted()
        {
            _cluster.LogInfo("Exiting completed.");
            // ExitingCompleted sent via CoordinatedShutdown to continue the leaving process.
            _exitingTasksInProgress = false;
            // mark as seen
            _membershipState = _membershipState.Seen();
            AssertLatestGossip();
            PublishMembershipState(); // exiting completed

            // Let others know (best effort) before shutdown. Otherwise they will not see
            // convergence of the Exiting state until they have detected this node as
            // unreachable and the required downing has finished. They will still need to detect
            // unreachable, but Exiting unreachable will be removed without downing, i.e.
            // normally the leaving of a leader will be graceful without the need
            // for downing. However, if those final gossip messages never arrive it is
            // alright to require the downing, because that is probably caused by a
            // network failure anyway.
            SendGossipRandom(NumberOfGossipsBeforeShutdownWhenLeaderExits);

            // send ExitingConfirmed to two potential leaders
            var membersWithoutSelf = LatestGossip.Members.Where(m => !m.UniqueAddress.Equals(SelfUniqueAddress))
                .ToImmutableSortedSet(Member.LeaderStatusOrdering);

            var leader = _membershipState.LeaderOf(membersWithoutSelf);
            if (leader != null)
            {
                ClusterCore(leader.Address).Tell(new InternalClusterAction.ExitingConfirmed(SelfUniqueAddress));

                var leader2 =
                    _membershipState.LeaderOf(membersWithoutSelf.Where(x => !x.UniqueAddress.Equals(leader)));
                if (leader2 != null)
                {
                    ClusterCore(leader2.Address)
                        .Tell(new InternalClusterAction.ExitingConfirmed(SelfUniqueAddress));
                }
            }

            Shutdown();
        }

        private void ReceiveExitingConfirmed(UniqueAddress node)
        {
            _cluster.LogInfo("Exiting confirmed [{0}]", node.Address);
            _exitingConfirmed = _exitingConfirmed.Add(node);
        }

        private void CleanupExitingConfirmed()
        {
            // in case the actual removal was performed by another leader node
            if (_exitingConfirmed.Count != 0)
                _exitingConfirmed = _exitingConfirmed
                    .Where(n => LatestGossip.Members.Any(m => m.UniqueAddress.Equals(n))).ToImmutableHashSet();
        }

        /// <summary>
        /// This method is called when a member sees itself as Exiting or Down.
        /// </summary>
        private void Shutdown()
        {
            _cluster.Shutdown();
        }

        /// <summary>
        /// State transition to DOWN.
        /// Its status is set to DOWN.The node is also removed from the `seen` table.
        /// The node will eventually be removed by the leader, and only after removal a new node with same address can
        /// join the cluster through the normal joining procedure.
        /// </summary>
        /// <param name="address">The address of the member that will be downed.</param>
        private void Downing(Address address)
        {
            var localGossip = LatestGossip;
            var localMembers = localGossip.Members;
            var localReachability = _membershipState.DcReachability;

            // check if the node to DOWN is in the 'members' set
            var member = localMembers.FirstOrDefault(m => m.Address == address);
            if (member != null && member.Status != MemberStatus.Down)
            {
                if (localReachability.IsReachable(member.UniqueAddress))
                    _cluster.LogInfo("Marking node [{0}] as [{1}]", member.Address, MemberStatus.Down);
                else
                    _cluster.LogInfo("Marking unreachable node [{0}] as [{1}]", member.Address, MemberStatus.Down);

                var newGossip = localGossip.MarkAsDown(member);
                UpdateLatestGossip(newGossip);
                PublishMembershipState(); // downing
            }
            else if (member != null)
            {
                // already down
            }
            else
            {
                _cluster.LogInfo("Ignoring down of unknown node [{0}]", address);
            }
        }

        private void Quarantined(UniqueAddress node)
        {
            var localGossip = LatestGossip;
            if (localGossip.HasMember(node))
            {
                var newReachability = localGossip.Overview.Reachability.Terminated(SelfUniqueAddress, node);
                var newOverview = localGossip.Overview.Copy(reachability: newReachability);
                var newGossip = localGossip.Copy(overview: newOverview);
                UpdateLatestGossip(newGossip);

                _log.Warning(
                    "Cluster Node [{0}] - Marking node as TERMINATED [{1}], due to quarantine. Node roles [{2}]",
                    Self, node.Address, string.Join(",", _cluster.SelfRoles));
                PublishMembershipState(); // quarantined
                Downing(node.Address);
            }
        }

        private void ReceiveGossipStatus(GossipStatus status)
        {
            var from = status.From;
            if (!LatestGossip.HasMember(from))
                _cluster.LogInfo("Ignoring received gossip status from unknown [{0}]", from);
            if (!LatestGossip.Overview.Reachability.IsReachable(SelfUniqueAddress, from))
                _cluster.LogInfo("Ignoring received gossip status from unreachable [{0}]", from);
            else
            {
                var comparison = status.Version.CompareTo(LatestGossip.Version);
                switch (comparison)
                {
                    case VectorClock.Ordering.Same: break; //same version
                    case VectorClock.Ordering.After:
                        GossipStatusTo(from, Sender);
                        break; //remote is newer
                    default:
                        GossipTo(from, Sender);
                        break; //conflicting or local is newer
                }
            }
        }

        /// <summary>
        /// The types of gossip actions that receive gossip has performed.
        /// </summary>
        public enum ReceiveGossipType
        {
            /// <summary>
            /// Gossip is ignored because node was not part of cluster, unreachable, etc..
            /// </summary>
            Ignored,

            /// <summary>
            /// Gossip received is older than what we currently have
            /// </summary>
            Older,

            /// <summary>
            /// Gossip received is newer than what we currently have
            /// </summary>
            Newer,

            /// <summary>
            /// Gossip received is same as what we currently have
            /// </summary>
            Same,

            /// <summary>
            /// Gossip received is concurrent with what we haved, and then merged.
            /// </summary>
            Merge
        }

        /// <summary>
        /// The types of gossip actions that receive gossip has performed.
        /// </summary>
        /// <param name="envelope">The gossip payload.</param>
        /// <returns>A command indicating how the gossip should be handled.</returns>
        private ReceiveGossipType ReceiveGossip(GossipEnvelope envelope)
        {
            var from = envelope.From;
            var remoteGossip = envelope.Gossip;
            var localGossip = LatestGossip;

            if (ReferenceEquals(remoteGossip, Gossip.Empty))
            {
                _log.Debug("Cluster Node [{0}] - Ignoring received gossip from [{1}] to protect against overload",
                    _cluster.SelfAddress, from);
                return ReceiveGossipType.Ignored;
            }

            if (!envelope.To.Equals(SelfUniqueAddress))
            {
                _cluster.LogInfo("Ignoring received gossip intended for someone else, from [{0}] to [{1}]",
                    from.Address, envelope.To);
                return ReceiveGossipType.Ignored;
            }

            if (!localGossip.HasMember(from))
            {
                _cluster.LogInfo("Cluster Node [{0}] - Ignoring received gossip from unknown [{1}]",
                    _cluster.SelfAddress, from);
                return ReceiveGossipType.Ignored;
            }

            if (!localGossip.IsReachable(SelfUniqueAddress, from))
            {
                _cluster.LogInfo("Ignoring received gossip from unreachable [{0}]", from);
                return ReceiveGossipType.Ignored;
            }

            if (remoteGossip.Members.All(m => !m.UniqueAddress.Equals(SelfUniqueAddress)))
            {
                _cluster.LogInfo("Ignoring received gossip that does not contain myself, from [{0}]", from);
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
                    talkback = !_exitingTasksInProgress && !remoteGossip.SeenByNode(SelfUniqueAddress);
                    winningGossip = remoteGossip.MergeSeen(localGossip);
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
                    talkback = !_exitingTasksInProgress && !remoteGossip.SeenByNode(SelfUniqueAddress);
                    gossipType = ReceiveGossipType.Newer;
                    break;
                default:
                    // conflicting versions, merge
                    // We can see that a removal was done when it is not in one of the gossips has status
                    // Down or Exiting in the other gossip.
                    // Perform the same pruning (clear of VectorClock) as the leader did when removing a member.
                    // Removal of member itself is handled in merge (pickHighestPriority)
                    var prunedLocalGossip = localGossip.Members.Aggregate(localGossip, (gossip, member) =>
                    {
                        if (MembershipState.IsRemoveUnreachableWithMemberStatus(member.Status) && !remoteGossip.Members.Contains(member))
                        {
                            _log.Debug("Cluster Node [{0}] - Pruned conflicting local gossip: {1}", _cluster.SelfAddress, member);
                            return gossip.Prune(VectorClock.Node.Create(Gossip.VectorClockName(member.UniqueAddress)));
                        }
                        else return gossip;
                    });

                    var prunedRemoteGossip = remoteGossip.Members.Aggregate(remoteGossip, (gossip, member) =>
                    {
                        if (MembershipState.IsRemoveUnreachableWithMemberStatus(member.Status) && !localGossip.Members.Contains(member))
                        {
                            _log.Debug("Cluster Node [{0}] - Pruned conflicting remote gossip: {1}", _cluster.SelfAddress, member);
                            return gossip.Prune(VectorClock.Node.Create(Gossip.VectorClockName(member.UniqueAddress)));
                        }
                        else return gossip;
                    });

                    //conflicting versions, merge
                    winningGossip = prunedRemoteGossip.Merge(prunedLocalGossip);
                    talkback = true;
                    gossipType = ReceiveGossipType.Merge;
                    break;
            }

            // Don't mark gossip state as seen while exiting is in progress, e.g.
            // shutting down singleton actors. This delays removal of the member until
            // the exiting tasks have been completed.
            _membershipState = _membershipState.Copy(
                latestGossip: _exitingTasksInProgress ? winningGossip : winningGossip.Seen(SelfUniqueAddress));
            AssertLatestGossip();

            // for all new joining nodes we remove them from the failure detector
            foreach (var node in LatestGossip.Members)
            {
                if (!localGossip.Members.Contains(node))
                {
                    _cluster.FailureDetector.Remove(node.Address);
                    _cluster.CrossDcFailureDetector.Remove(node.Address);
                }
            }

            _log.Debug("Cluster Node [{0}] - Receiving gossip from [{1}]", _cluster.SelfAddress, from);

            if (comparison == VectorClock.Ordering.Concurrent && _cluster.Settings.Debug.VerboseGossipLogging)
            {
                _log.Debug(
                    @"Couldn't establish a causal relationship between ""remote"" gossip and ""local"" gossip - Remote[{0}] - Local[{1}] - merged them into [{2}]",
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

            PublishMembershipState(); // receive gossip

            var selfStatus = LatestGossip.GetMember(SelfUniqueAddress).Status;
            if (selfStatus == MemberStatus.Exiting && !_exitingTasksInProgress)
            {
                // ExitingCompleted will be received via CoordinatedShutdown to continue
                // the leaving process. Meanwhile the gossip state is not marked as seen.
                _exitingTasksInProgress = true;
                _cluster.LogInfo("Exiting, starting coordinated shutdown.");
                _selfExiting.TrySetResult(Done.Instance);
                _coordShutdown.Run(CoordinatedShutdown.Reason.ClusterLeaving);
            }

            if (talkback)
            {
                // send back gossip to sender() when sender() had different view, i.e. merge, or sender() had
                // older or sender() had newer
                GossipTo(from, Sender);
            }

            return gossipType;
        }

        /// <summary>
        /// Sends gossip and schedules two future intervals for more gossip
        /// </summary>
        private void GossipTick()
        {
            SendGossip();
            if (IsGossipSpeedupNeeded())
            {
                _cluster.Scheduler.ScheduleTellOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks / 3), Self,
                    InternalClusterAction.Tick.GossipSpeedupTick, ActorRefs.NoSender);
                _cluster.Scheduler.ScheduleTellOnce(new TimeSpan(_cluster.Settings.GossipInterval.Ticks * 2 / 3),
                    Self, InternalClusterAction.Tick.GossipSpeedupTick, ActorRefs.NoSender);
            }
        }

        private void GossipSpeedupTick()
        {
            if (IsGossipSpeedupNeeded()) SendGossip();
        }

        private bool IsGossipSpeedupNeeded() => LatestGossip.IsMultiDc
            ? LatestGossip.Overview.Seen.Count(_membershipState.IsInSameDc) <
              LatestGossip.Members.Count(m => m.DataCenter == _cluster.SelfDataCenter) / 2
            : LatestGossip.Overview.Seen.Count < LatestGossip.Members.Count / 2;

        /// <summary>
        /// Sends full gossip to `n` other random members.
        /// </summary>
        private void SendGossipRandom(int n)
        {
            if (!IsSingletonCluster && n > 0)
            {
                foreach (var address in _gossipTargetSelector.RandomNodesForFullGossip(_membershipState, n))
                {
                    GossipTo(address);
                }
            }
        }

        /// <summary>
        /// Initiates a new round of gossip.
        /// </summary>
        private void SendGossip()
        {
            if (!IsSingletonCluster)
            {
                var localGossip = LatestGossip;
                var peer = _gossipTargetSelector.GossipTarget(_membershipState);
                if (peer != null)
                {
                    if (!_membershipState.IsInSameDc(peer) || LatestGossip.SeenByNode(peer))
                    {
                        // avoid transferring the full state if possible
                        GossipStatusTo(peer);
                    }
                    else
                    {
                        GossipTo(peer);
                    }
                }
                else
                {
                    // nothing to see here
                    if (_cluster.Settings.Debug.VerboseGossipLogging)
                    {
                        _log.Debug("Cluster Node [{0}] dc [{1}] will not gossip this round", SelfAddress,
                            _cluster.SelfDataCenter);
                    }
                }
            }
        }

        /// <summary>
        /// Runs periodic leader actions, such as member status transitions, assigning partitions etc.
        /// </summary>
        private void LeaderActions()
        {
            if (_membershipState.IsLeader(SelfUniqueAddress))
            {
                // only run the leader actions if we are the LEADER
                const int firstNotice = 20;
                const int periodicNotice = 60;
                if (_membershipState.Convergence(_exitingConfirmed))
                {
                    if (_leaderActionCounter >= firstNotice)
                        _cluster.LogInfo("Leader can perform its duties again");
                    _leaderActionCounter = 0;
                    LeaderActionsOnConvergence();
                }
                else
                {
                    _leaderActionCounter++;

                    if (_cluster.Settings.AllowWeaklyUpMembers && _leaderActionCounter >= 3)
                        MoveJoiningToWeaklyUp();

                    if (_leaderActionCounter == firstNotice || _leaderActionCounter % periodicNotice == 0)
                    {
                        if (_log.IsInfoEnabled)
                        {
                            _cluster.LogInfo(
                                "Leader can currently not perform its duties, reachability status: [{0}], member status: [{1}]",
                                _membershipState.DcReachabilityExcludingDownedObservers,
                                string.Join(", ", LatestGossip.Members
                                    .Where(m => m.DataCenter == _selfDataCenter)
                                    .Select(m =>
                                        $"({m.Address} {m.Status} seen={LatestGossip.SeenByNode(m.UniqueAddress)})")));
                        }
                    }
                }
            }

            CleanupExitingConfirmed();
            ShutdownSelfWhenDown();
        }

        private void ShutdownSelfWhenDown()
        {
            if (LatestGossip.GetMember(SelfUniqueAddress).Status == MemberStatus.Down)
            {
                // When all reachable have seen the state this member will shutdown itself when it has
                // status Down. The down commands should spread before we shutdown.
                var unreachable = _membershipState.DcReachability.AllUnreachableOrTerminated;
                var downed = _membershipState.DcMembers
                    .Where(m => m.Status == MemberStatus.Down)
                    .Select(m => m.UniqueAddress);

                if (downed.All(node => unreachable.Contains(node) || LatestGossip.SeenByNode(node)))
                {
                    // the reason for not shutting down immediately is to give the gossip a chance to spread
                    // the downing information to other downed nodes, so that they can shutdown themselves
                    _cluster.LogInfo("Shutting down myself");

                    // not crucial to send gossip, but may speedup removal since fallback to failure detection is not needed
                    // if other downed know that this node has seen the version
                    SendGossipRandom(MaxGossipsBeforeShuttingDownMyself);
                    Shutdown();
                }
            }
        }

        /// <summary>
        /// If akka.cluster.min-rn-of-members or akka.cluster.roles.[rolename].min-nr-of-members is set,
        /// this function will check to see if that threshold is met.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the setting isn't enabled or is satisfied. 
        /// <c>false</c> is the setting is enabled and unsatisfied.
        /// </returns>
        private bool IsMinNrOfMembersFulfilled() =>
            LatestGossip.Members.Count >= _cluster.Settings.MinNrOfMembers
            && _cluster.Settings.MinNrOfMembersOfRole.All(x =>
                LatestGossip.Members.Count(c => c.HasRole(x.Key)) >= x.Value);

        /// <summary>
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
        /// </summary>
        private void LeaderActionsOnConvergence()
        {
            var latestGossip = _membershipState.LatestGossip;

            var removedUnreachable = _membershipState.DcReachability.AllUnreachableOrTerminated
                .Select(latestGossip.GetMember)
                .Where(m => m.DataCenter == _selfDataCenter && MembershipState.IsRemoveUnreachableWithMemberStatus(m.Status))
                .ToImmutableHashSet();

            var removedExitingConfirmed =
                _exitingConfirmed.Where(x =>
                    {
                        var member = latestGossip.GetMember(x);
                        return member.Status == MemberStatus.Exiting && member.DataCenter == _selfDataCenter;
                    })
                    .ToImmutableHashSet();

            var removedOtherDc = latestGossip.IsMultiDc
                ? latestGossip.Members
                    .Where(m => m.DataCenter != _selfDataCenter && MembershipState.IsRemoveUnreachableWithMemberStatus(m.Status))
                    .ToImmutableHashSet()
                : ImmutableHashSet<Member>.Empty;


            var enoughMembers = IsMinNrOfMembersFulfilled();

            bool IsJoiningUp(Member member) =>
                (member.Status == MemberStatus.Joining || member.Status == MemberStatus.WeaklyUp) && enoughMembers;

            var upNumber = 0;
            var changedMembers = latestGossip.Members.Select(m =>
            {
                if (IsJoiningUp(m))
                {
                    // Move JOINING => UP (once all nodes have seen that this node is JOINING, i.e. we have a convergence)
                    // and minimum number of nodes have joined the cluster
                    if (upNumber == 0)
                    {
                        // It is alright to use same upNumber as already used by a removed member, since the upNumber
                        // is only used for comparing age of current cluster members (Member.isOlderThan)
                        var youngest = _membershipState.YoungestMember;
                        upNumber = 1 + (youngest.UpNumber == int.MaxValue ? 0 : youngest.UpNumber);
                    }
                    else
                    {
                        upNumber += 1;
                    }

                    return m.CopyUp(upNumber);
                }
                else if (m.Status == MemberStatus.Leaving)
                {
                    // Move LEAVING => EXITING (once we have a convergence on LEAVING
                    // *and* if we have a successful partition handoff)
                    return m.Copy(MemberStatus.Exiting);
                }
                else return null;
            }).Where(m => m != null).ToImmutableSortedSet();

            Gossip updatedGossip;
            if (!removedUnreachable.IsEmpty || !removedExitingConfirmed.IsEmpty || !changedMembers.IsEmpty ||
                !removedOtherDc.IsEmpty)
            {
                // replace changed members
                var removed = removedUnreachable
                    .Select(u => u.UniqueAddress)
                    .ToImmutableHashSet()
                    .Union(removedExitingConfirmed)
                    .Union(removedOtherDc.Select(m => m.UniqueAddress));

                var newGossip = latestGossip.Update(changedMembers).RemoveAll(removed, DateTime.UtcNow);

                if (!_exitingTasksInProgress &&
                    newGossip.GetMember(SelfUniqueAddress).Status == MemberStatus.Exiting)
                {
                    // Leader is moving itself from Leaving to Exiting.
                    // ExitingCompleted will be received via CoordinatedShutdown to continue
                    // the leaving process. Meanwhile the gossip state is not marked as seen.

                    _exitingTasksInProgress = true;
                    _cluster.LogInfo("Exiting (leader), starting coordinated shutdown.");
                    _selfExiting.TrySetResult(Done.Instance);
                    _coordShutdown.Run(CoordinatedShutdown.Reason.ClusterLeaving);
                }

                _exitingConfirmed = _exitingConfirmed.Except(removedExitingConfirmed);

                // log status changes
                foreach (var m in changedMembers)
                    _cluster.LogInfo("Leader is moving node [{0}] to [{1}]", m.Address, m.Status);

                //log the removal of unreachable nodes
                foreach (var m in removedUnreachable)
                {
                    var status = m.Status == MemberStatus.Exiting ? "exiting" : "unreachable";
                    _log.Info("Leader is removing {0} node [{1}]", status, m.Address);
                }

                foreach (var m in removedExitingConfirmed)
                {
                    _log.Info("Leader is removing confirmed Exiting node [{0}]", m.Address);
                }

                foreach (var m in removedOtherDc)
                {
                    _cluster.LogInfo("Leader is removing {0} node [{1}] in DC [{2}]", m.Status, m.Address,
                        m.DataCenter);
                }

                updatedGossip = newGossip;
            }
            else updatedGossip = latestGossip;

            var pruned =
                updatedGossip.PruneTombstones(DateTime.UtcNow - _cluster.Settings.PruneGossipTombstonesAfter);
            if (!ReferenceEquals(latestGossip, pruned))
            {
                UpdateLatestGossip(pruned);
                PublishMembershipState(); // leader actions on convergence
            }
        }

        private void MoveJoiningToWeaklyUp()
        {
            var localGossip = _membershipState.LatestGossip;
            var localMembers = localGossip.Members;
            var enoughMembers = IsMinNrOfMembersFulfilled();

            bool IsJoiningToWeaklyUp(Member m) => m.DataCenter == _selfDataCenter
                                                  && m.Status == MemberStatus.Joining
                                                  && enoughMembers
                                                  && _membershipState.DcReachabilityExcludingDownedObservers
                                                      .IsReachable(m.UniqueAddress);

            var changedMembers = localMembers
                .Where(IsJoiningToWeaklyUp)
                .Select(m => m.Copy(MemberStatus.WeaklyUp))
                .ToImmutableSortedSet();

            if (!changedMembers.IsEmpty)
            {
                // replace changed members
                var newMembers = Member.PickNextTransition(localMembers, changedMembers);
                var newGossip = localGossip.Copy(members: newMembers);
                UpdateLatestGossip(newGossip);

                // log status change
                foreach (var m in changedMembers)
                {
                    _cluster.LogInfo("Leader is moving node [{0}] to [{1}]", m.Address, m.Status);
                }

                PublishMembershipState(); // joining -> weakly up
            }
        }

        /// <summary>
        /// Reaps the unreachable members according to the failure detector's verdict.
        /// </summary>
        private void ReapUnreachableMembers()
        {
            if (!IsSingletonCluster)
            {
                // only scrutinize if we are a non-singleton cluster

                var localGossip = _membershipState.LatestGossip;
                var localOverview = localGossip.Overview;
                var localMembers = localGossip.Members;

                bool IsAvailable(Member member) =>
                    member.DataCenter == _selfDataCenter
                        ? _cluster.FailureDetector.IsAvailable(member.Address)
                        : _cluster.CrossDcFailureDetector.IsAvailable(member.Address);
                
                var newlyDetectedUnreachableMembers =
                    localMembers.Where(member => !(
                            member.UniqueAddress == SelfUniqueAddress ||
                            localOverview.Reachability.Status(SelfUniqueAddress, member.UniqueAddress) != Reachability.ReachabilityStatus.Reachable ||
                            IsAvailable(member)))
                        .ToImmutableSortedSet();

                var newlyDetectedReachableMembers = localOverview.Reachability.AllUnreachableFrom(SelfUniqueAddress)
                    .Where(node => node != SelfUniqueAddress && IsAvailable(localGossip.GetMember(node)))
                    .Select(localGossip.GetMember)
                    .ToImmutableHashSet();

                if (!newlyDetectedUnreachableMembers.IsEmpty || !newlyDetectedReachableMembers.IsEmpty)
                {
                    var newReachability1 = newlyDetectedUnreachableMembers.Aggregate(
                        localOverview.Reachability,
                        (reachability, m) => reachability.Unreachable(SelfUniqueAddress, m.UniqueAddress));

                    var newReachability2 = newlyDetectedReachableMembers.Aggregate(
                        newReachability1,
                        (reachability, m) => reachability.Reachable(SelfUniqueAddress, m.UniqueAddress));

                    if (!ReferenceEquals(newReachability2, localOverview.Reachability))
                    {
                        var newOverview = localOverview.Copy(reachability: newReachability2);
                        var newGossip = localGossip.Copy(overview: newOverview);

                        UpdateLatestGossip(newGossip);

                        var partitioned =
                            newlyDetectedUnreachableMembers.Partition(m => m.Status == MemberStatus.Exiting);
                        var exiting = partitioned.Item1;
                        var nonExiting = partitioned.Item2;

                        if (!nonExiting.IsEmpty)
                            _log.Warning(
                                "Cluster Node [{0}] - Marking node(s) as UNREACHABLE [{1}]. Node roles [{2}]",
                                _cluster.SelfAddress,
                                nonExiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b),
                                string.Join(",", _cluster.SelfRoles));

                        if (!exiting.IsEmpty)
                            _cluster.LogInfo(
                                "Cluster Node [{0}] - Marking exiting node(s) as UNREACHABLE [{1}]. This is expected and they will be removed.",
                                _cluster.SelfAddress,
                                exiting.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b));

                        if (!newlyDetectedReachableMembers.IsEmpty)
                            _cluster.LogInfo("Marking node(s) as REACHABLE [{0}]. Node roles [{1}]",
                                newlyDetectedReachableMembers.Select(m => m.ToString())
                                    .Aggregate((a, b) => a + ", " + b), string.Join(",", _cluster.SelfRoles));

                        PublishMembershipState(); // reap unreachable
                    }
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> if this is a one node cluster. <c>false</c> otherwise.
        /// </summary>
        private bool IsSingletonCluster => LatestGossip.IsSingletonCluster;

        /// <summary>
        /// needed for tests
        /// </summary>
        /// <param name="address">TBD</param>
        private void SendGossipTo(Address address)
        {
            foreach (var m in LatestGossip.Members)
            {
                if (m.Address.Equals(address))
                    GossipTo(m.UniqueAddress);
            }
        }

        /// <summary>
        /// Gossips latest gossip to a node.
        /// </summary>
        /// <param name="node">The address of the node we want to send gossip to.</param>
        private void GossipTo(UniqueAddress node)
        {
            if (_membershipState.IsValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipEnvelope(SelfUniqueAddress, node, LatestGossip));
        }

        private void GossipTo(UniqueAddress node, IActorRef destination)
        {
            if (_membershipState.IsValidNodeForGossip(node))
                destination.Tell(new GossipEnvelope(SelfUniqueAddress, node, LatestGossip));
        }

        private void GossipStatusTo(UniqueAddress node)
        {
            if (_membershipState.IsValidNodeForGossip(node))
                ClusterCore(node.Address).Tell(new GossipStatus(SelfUniqueAddress, LatestGossip.Version));
        }

        private void GossipStatusTo(UniqueAddress node, IActorRef destination)
        {
            if (_membershipState.IsValidNodeForGossip(node))
                destination.Tell(new GossipStatus(SelfUniqueAddress, LatestGossip.Version));
        }

        /// <summary>
        /// Updates the local gossip with the latest received from over the network.
        /// </summary>
        /// <param name="newGossip">The new gossip to merge with our own.</param>
        private void UpdateLatestGossip(Gossip gossip)
        {
            // Updating the vclock version for the changes
            var versionedGossip = gossip.Increment(_vclockNode);

            // Don't mark gossip state as seen while exiting is in progress, e.g.
            // shutting down singleton actors. This delays removal of the member until
            // the exiting tasks have been completed.
            var newGossip = _exitingTasksInProgress
                ? versionedGossip.ClearSeen()
                : versionedGossip.OnlySeen(SelfUniqueAddress);

            _membershipState = _membershipState.Copy(latestGossip: newGossip);
            AssertLatestGossip();
        }

        /// <summary>
        /// Asserts that the gossip is valid and only contains information for current members of the cluster.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the VectorClock is corrupt and has not been pruned properly.</exception>
        private void AssertLatestGossip()
        {
            if (Cluster.IsAssertInvariantsEnabled &&
                LatestGossip.Version.Versions.Count > LatestGossip.Members.Count)
                throw new InvalidOperationException(
                    $"Too many vector clock entries in gossip state {LatestGossip}");
        }

        /// <summary>
        /// Publishes membership state to other nodes in the cluster.
        /// </summary>
        private void PublishMembershipState()
        {
            if (_cluster.Settings.Debug.VerboseGossipLogging)
                _log.Debug("Cluster Node [{0}] dc [{1}] - New gossip published [{2}]", SelfAddress,
                    _cluster.Settings.SelfDataCenter, _membershipState.LatestGossip);

            _publisher.Tell(new InternalClusterAction.PublishChanges(_membershipState));

            if (_cluster.Settings.PublishStatsInterval == TimeSpan.Zero)
                PublishInternalStats();
        }

        private void PublishInternalStats()
        {
            var vclockStats = new VectorClockStats(
                versionSize: LatestGossip.Version.Versions.Count,
                seenLatest: LatestGossip.Members.Count(m => LatestGossip.SeenByNode(m.UniqueAddress)));

            _publisher.Tell(new ClusterEvent.CurrentInternalStats(_gossipStats, vclockStats));
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
    internal sealed class FirstSeedNodeProcess : ActorBase
    {
        readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly ImmutableList<Address> _seeds;
        private ImmutableList<Address> _remainingSeeds;
        readonly Address _selfAddress;
        readonly Cluster _cluster;
        readonly Deadline _timeout;
        readonly ICancelable _retryTaskToken;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seeds">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the number of specified <paramref name="seeds"/> is less than or equal to 1
        /// or the first listed seed is a reference to the <see cref="IUntypedActorContext.System"/>'s address.
        /// </exception>
        public FirstSeedNodeProcess(ImmutableList<Address> seeds)
        {
            _seeds = seeds;
            _cluster = Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;

            if (seeds.Count <= 1 || seeds.First() != _selfAddress)
                throw new ArgumentException("Join seed node should not be done");

            _remainingSeeds = seeds.Remove(_selfAddress);
            _timeout = Deadline.Now + _cluster.Settings.SeedNodeTimeout;

            _retryTaskToken = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1), Self, InternalClusterAction.JoinSeedNode.Instance, Self);

            Self.Tell(InternalClusterAction.JoinSeedNode.Instance);
        }

        /// <inheritdoc cref="ActorBase"/>
        protected override void PostStop() => _retryTaskToken.Cancel();

        /// <inheritdoc cref="ActorBase"/>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InternalClusterAction.JoinSeedNode _:
                    if (_timeout.HasTimeLeft)
                    {
                        // send InitJoin to remaining seed nodes (except myself)
                        foreach (var seed in _remainingSeeds)
                        {
                            Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(seed))
                                .Tell(InternalClusterAction.InitJoin.Instance);
                        }
                    }
                    else
                    {
                        if (_log.IsDebugEnabled)
                            _log.Debug("Couldn't join other seed nodes, will join myself. seed-nodes=[{0}]",
                                string.Join(", ", _seeds));

                        // no InitJoinAck received, initialize new cluster by joining myself
                        Context.Parent.Tell(new ClusterUserAction.JoinTo(_selfAddress));
                        Context.Stop(Self);
                    }

                    return true;
                case InternalClusterAction.InitJoinAck ack:
                    _cluster.LogInfo("Received InitJoinAck message from [{0}] to [{1}]", Sender, _selfAddress);
                    // first InitJoinAck reply, join existing cluster
                    Context.Parent.Tell(new ClusterUserAction.JoinTo(ack.Address));
                    Context.Stop(Self);
                    return true;
                case InternalClusterAction.InitJoinNack nack:
                    _cluster.LogInfo("Received InitJoinNack message from [{0}] to [{1}]", Sender, _selfAddress);
                    _remainingSeeds = _remainingSeeds.Remove(nack.Address);
                    if (_remainingSeeds.IsEmpty)
                    {
                        // initialize new cluster by joining myself when nacks from all other seed nodes
                        Context.Parent.Tell(new ClusterUserAction.JoinTo(_selfAddress));
                        Context.Stop(Self);
                    }

                    return true;
                default: return false;
            }
        }
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
    internal sealed class JoinSeedNodeProcess : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly ImmutableList<Address> _seeds;
        private readonly Address _selfAddress;
        private int _attempts = 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seeds">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the list of specified <paramref name="seeds"/> is empty
        /// or the first listed seed is a reference to the <see cref="IUntypedActorContext.System"/>'s address.
        /// </exception>
        public JoinSeedNodeProcess(ImmutableList<Address> seeds)
        {
            _selfAddress = Cluster.Get(Context.System).SelfAddress;
            _seeds = seeds;
            if (seeds.IsEmpty || seeds.Head() == _selfAddress)
                throw new ArgumentException("Join seed node should not be done");
            Context.SetReceiveTimeout(Cluster.Get(Context.System).Settings.SeedNodeTimeout);
        }

        /// <inheritdoc cref="ActorBase"/>
        protected override void PreStart() => Self.Tell(InternalClusterAction.JoinSeedNode.Instance);

        /// <inheritdoc cref="ActorBase"/>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InternalClusterAction.JoinSeedNode _:
                    //send InitJoin to all seed nodes (except myself)
                    _attempts++;
                    foreach (var seed in _seeds)
                    {
                        if (seed != _selfAddress)
                        {
                            var path = Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(seed));
                            path.Tell(InternalClusterAction.InitJoin.Instance);
                        }
                    }

                    return true;
                case InternalClusterAction.InitJoinAck ack:
                    //first InitJoinAck reply
                    Context.Parent.Tell(new ClusterUserAction.JoinTo(ack.Address));
                    Context.Become(Done);
                    return true;
                case InternalClusterAction.InitJoinNack _: return true;
                case ReceiveTimeout _:
                    if (_attempts >= 2)
                        _log.Warning(
                            "Couldn't join seed nodes after [{0}] attempts, will try again. seed-nodes=[{1}]",
                            _attempts, string.Join(",", _seeds.Where(x => !x.Equals(_selfAddress))));
                    //no InitJoinAck received - try again
                    Self.Tell(InternalClusterAction.JoinSeedNode.Instance);
                    return true;
                default: return false;
            }
        }

        private bool Done(object message)
        {
            switch (message)
            {
                case InternalClusterAction.InitJoinAck _:
                    //already received one, skip the rest
                    return true;
                case ReceiveTimeout _:
                    Context.Stop(Self);
                    return true;
                default: return false;
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
            long newerCount = 0L,
            long olderCount = 0L)
        {
            ReceivedGossipCount = receivedGossipCount;
            MergeCount = mergeCount;
            SameCount = sameCount;
            NewerCount = newerCount;
            OlderCount = olderCount;
        }

        public GossipStats IncrementMergeCount() =>
            Copy(mergeCount: MergeCount + 1, receivedGossipCount: ReceivedGossipCount + 1);

        public GossipStats IncrementSameCount() =>
            Copy(sameCount: SameCount + 1, receivedGossipCount: ReceivedGossipCount + 1);

        public GossipStats IncrementNewerCount() =>
            Copy(newerCount: NewerCount + 1, receivedGossipCount: ReceivedGossipCount + 1);

        public GossipStats IncrementOlderCount() =>
            Copy(olderCount: OlderCount + 1, receivedGossipCount: ReceivedGossipCount + 1);

        public GossipStats Copy(long? receivedGossipCount = null, long? mergeCount = null, long? sameCount = null,
            long? newerCount = null, long? olderCount = null) =>
            new GossipStats(receivedGossipCount ?? ReceivedGossipCount,
                mergeCount ?? MergeCount,
                sameCount ?? SameCount,
                newerCount ?? NewerCount,
                olderCount ?? OlderCount);

        #region Operator overloads

        /// <summary>
        /// Combines two statistics together to create new statistics.
        /// </summary>
        /// <param name="a">The first set of statistics to combine.</param>
        /// <param name="b">The second statistics to combine.</param>
        /// <returns>A new <see cref="GossipStats"/> that is a combination of the two specified statistics.</returns>
        public static GossipStats operator +(GossipStats a, GossipStats b) =>
            new GossipStats(a.ReceivedGossipCount + b.ReceivedGossipCount,
                a.MergeCount + b.MergeCount,
                a.SameCount + b.SameCount,
                a.NewerCount + b.NewerCount,
                a.OlderCount + b.OlderCount);

        /// <summary>
        /// Decrements the first set of statistics, <paramref name="a"/>, using the second set of statistics, <paramref name="b"/>.
        /// </summary>
        /// <param name="a">The set of statistics to decrement.</param>
        /// <param name="b">The set of statistics used to decrement.</param>
        /// <returns>A new <see cref="GossipStats"/> that is calculated by decrementing <paramref name="a"/> by <paramref name="b"/>.</returns>
        public static GossipStats operator -(GossipStats a, GossipStats b) =>
            new GossipStats(a.ReceivedGossipCount - b.ReceivedGossipCount,
                a.MergeCount - b.MergeCount,
                a.SameCount - b.SameCount,
                a.NewerCount - b.NewerCount,
                a.OlderCount - b.OlderCount);

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The supplied callback will be run once when the current cluster member has the same status.
    /// </summary>
    internal class OnMemberStatusChangedListener : ActorBase
    {
        private readonly Action _callback;
        private readonly MemberStatus _status;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Cluster _cluster;

        private Type To
        {
            get
            {
                switch (_status)
                {
                    case MemberStatus.Up: return typeof(ClusterEvent.MemberUp);
                    case MemberStatus.Removed: return typeof(ClusterEvent.MemberRemoved);
                    default:
                        throw new ArgumentException(
                            $"Expected Up or Removed in OnMemberStatusChangedListener, got [{_status}]");
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="callback">TBD</param>
        /// <param name="targetStatus">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="targetStatus"/> is invalid.
        /// Acceptable values are: <see cref="MemberStatus.Up"/> | <see cref="MemberStatus.Down"/>.
        /// </exception>
        public OnMemberStatusChangedListener(Action callback, MemberStatus targetStatus)
        {
            _callback = callback;
            _status = targetStatus;
            _cluster = Cluster.Get(Context.System);
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    if (state.Members.Any(IsTriggered))
                        Done();
                    return true;
                case ClusterEvent.MemberUp up:
                    if (IsTriggered(up.Member))
                        Done();
                    return true;
                case ClusterEvent.MemberRemoved removed:
                    if (IsTriggered(removed.Member))
                        Done();
                    return true;
                default: return false;
            }
        }

        /// <inheritdoc cref="ActorBase.PreStart"/>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the current status neither <see cref="MemberStatus.Up"/> or <see cref="MemberStatus.Down"/>.
        /// </exception>
        protected override void PreStart() => _cluster.Subscribe(Self, To);

        /// <inheritdoc cref="ActorBase.PostStop"/>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the current status neither <see cref="MemberStatus.Up"/> or <see cref="MemberStatus.Down"/>.
        /// </exception>
        protected override void PostStop()
        {
            // execute MemberRemoved hooks if we are shutting down
            if (_status == MemberStatus.Removed)
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
                _log.Error(ex, "[{0}] callback failed with [{1}]", To.Name, ex.Message);
            }
            finally
            {
                Context.Stop(Self);
            }
        }

        private bool IsTriggered(Member m) => m.UniqueAddress == _cluster.SelfUniqueAddress && m.Status == _status;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class VectorClockStats : IEquatable<VectorClockStats>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="versionSize">TBD</param>
        /// <param name="seenLatest">TBD</param>
        public VectorClockStats(int versionSize = 0, int seenLatest = 0)
        {
            VersionSize = versionSize;
            SeenLatest = seenLatest;
        }

        public int VersionSize { get; }
        public int SeenLatest { get; }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is VectorClockStats stats && Equals(stats);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (VersionSize * 397) ^ SeenLatest;
            }
        }

        public bool Equals(VectorClockStats other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return VersionSize == other.VersionSize && SeenLatest == other.SeenLatest;
        }
    }
}