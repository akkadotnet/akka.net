using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{
    /// <summary>
    /// Base interface for all cluster messages. All ClusterMessage's are serializable.
    /// </summary>
    public interface IClusterMessage
    {
    }

    //TODO: Xmldoc
    //TODO: Comment mentions JMX
    /// <summary>
    /// Cluster commands sent by the USER via
    /// [[akka.cluster.Cluster]] extension
    /// or JMX.
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
        internal sealed class Leave : BaseClusterUserAction
        {
            public Leave(Address address) : base(address)
            {}
        }

        /// <summary>
        /// Command to mark node as temporary down.
        /// </summary>
        internal sealed class Down : BaseClusterUserAction
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
        }

        /// <summary>
        /// Reply to Join
        /// </summary>
        internal sealed class Welcome
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
        /// The node sends `InitJoin` to all seed nodes, which replies
        /// with `InitJoinAck`. The first reply is used others are discarded.
        /// The node sends `Join` command to the seed node that replied first.
        /// If a node is uninitialized it will reply to `InitJoin` with
        /// `InitJoinNack`.
        /// </summary>
        internal class JoinSeenNode
        {
        }

        /// <summary>
        /// See JoinSeedNode
        /// </summary>
        internal class InitJoin : IClusterMessage
        {
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
        }

        /// <summary>
        /// Marker interface for periodic tick messages
        /// </summary>
        internal interface ITick {}

        internal class GossipTick : ITick {}

        internal class GossipSpeedupTick : ITick { }

        internal class ReapUnreachableTick : ITick { }

        internal class MetricsTick : ITick { }

        internal class LeaderActionsTick : ITick { }

        internal class PublishStatsTick : ITick { }

        internal class GetClusterCoreRef
        {
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
        public ClusterDaemon(ClusterSettings settings)
        {
            throw new NotImplementedException();
        }

        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }

        public LoggingAdapter Log { get; private set; }
    }
}
