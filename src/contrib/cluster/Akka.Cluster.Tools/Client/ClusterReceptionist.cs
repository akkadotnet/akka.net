//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionist.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// Marker trait for remote messages with special serializer.
    /// </summary>
    public interface IClusterClientMessage { }

    /// <summary>
    /// Declares a super type for all events emitted by the <see cref="ClusterClientReceptionist"/>
    /// in relation to cluster clients being interacted with.
    /// </summary>
    public interface IClusterClientInteraction
    {
        IActorRef ClusterClient { get; }
    }

    /// <summary>
    /// Emitted to the Akka event stream when a cluster client has interacted with
    /// a receptionist.
    /// </summary>
    public sealed class ClusterClientUp : IClusterClientInteraction
    {
        public ClusterClientUp(IActorRef clusterClient)
        {
            ClusterClient = clusterClient;
        }

        public IActorRef ClusterClient { get; }
    }

    /// <summary>
    /// Emitted to the Akka event stream when a cluster client was previously connected
    /// but then not seen for some time.
    /// </summary>
    public sealed class ClusterClientUnreachable : IClusterClientInteraction
    {
        public ClusterClientUnreachable(IActorRef clusterClient)
        {
            ClusterClient = clusterClient;
        }

        public IActorRef ClusterClient { get; }
    }

    /// <summary>
    /// Subscribe to a cluster receptionist's client interactions where
    /// it is guaranteed that a sender receives the initial state
    /// of contact points prior to any events in relation to them
    /// changing.
    /// The sender will automatically become unsubscribed when it
    /// terminates.
    /// </summary>
    public sealed class SubscribeClusterClients
    {
        public static readonly SubscribeClusterClients Instance = new SubscribeClusterClients();
        private SubscribeClusterClients() { }
    }

    /// <summary>
    /// Explicitly unsubscribe from client interaction events.
    /// </summary>
    public sealed class UnsubscribeClusterClients
    {
        public static readonly UnsubscribeClusterClients Instance = new UnsubscribeClusterClients();
        private UnsubscribeClusterClients() { }
    }

    /// <summary>
    /// Get the cluster clients known to this receptionist. A <see cref="ClusterClients"/> message
    /// will be replied.
    /// </summary>
    public sealed class GetClusterClients
    {
        public static readonly GetClusterClients Instance = new GetClusterClients();
        private GetClusterClients() { }
    }

    /// <summary>
    /// The reply to <see cref="GetClusterClients"/>
    /// </summary>
    public sealed class ClusterClients
    {
        /// <summary>
        /// The reply to <see cref="GetClusterClients"/>
        /// </summary>
        /// <param name="clusterClientsList">The presently known list of cluster clients.</param>
        public ClusterClients(ImmutableHashSet<IActorRef> clusterClientsList)
        {
            ClusterClientsList = clusterClientsList;
        }

        public ImmutableHashSet<IActorRef> ClusterClientsList { get; }
    }

    /// <summary>
    /// <para>
    /// <see cref="ClusterClient"/> connects to this actor to retrieve. The <see cref="ClusterReceptionist"/> is
    /// supposed to be started on all nodes, or all nodes with specified role, in the cluster.
    /// The receptionist can be started with the <see cref="ClusterClientReceptionist.Get"/> or as an
    /// ordinary actor (use the factory method <see cref="ClusterReceptionist.Props"/>).
    /// </para>
    /// <para>
    /// The receptionist forwards messages from the client to the associated <see cref="DistributedPubSubMediator"/>,
    /// i.e. the client can send messages to any actor in the cluster that is registered in the
    /// <see cref="DistributedPubSubMediator"/>. Messages from the client are wrapped in <see cref="Send"/>, <see cref="SendToAll"/>
    /// or <see cref="Publish"/> with the semantics described in distributed publish/subscribe.
    /// </para>
    /// <para>
    /// Response messages from the destination actor are tunneled via the receptionist
    /// to avoid inbound connections from other cluster nodes to the client, i.e.
    /// the <see cref="ActorBase.Sender"/>, as seen by the destination actor, is not the client itself.
    /// The <see cref="ActorBase.Sender"/> of the response messages, as seen by the client, is preserved
    /// as the original sender, so the client can choose to send subsequent messages
    /// directly to the actor in the cluster.
    /// </para>
    /// </summary>
    public sealed class ClusterReceptionist : ActorBase
    {
        #region Messages

        [Serializable]
        internal sealed class GetContacts : IClusterClientMessage, IDeadLetterSuppression
        {
            public static readonly GetContacts Instance = new GetContacts();
            private GetContacts() { }
        }

        [Serializable]
        internal sealed class Contacts : IClusterClientMessage
        {
            public readonly ImmutableList<string> ContactPoints;

            public Contacts(ImmutableList<string> contactPoints)
            {
                ContactPoints = contactPoints;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;

                var other = obj as Contacts;
                if (other == null) return false;

                return ContactPoints.SequenceEqual(other.ContactPoints);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 19;
                    foreach (var foo in ContactPoints)
                    {
                        hash = hash * 31 + foo.GetHashCode();
                    }
                    return hash;
                }
            }
        }

        [Serializable]
        internal sealed class Heartbeat : IClusterClientMessage, IDeadLetterSuppression
        {
            public static readonly Heartbeat Instance = new Heartbeat();
            private Heartbeat() { }
        }

        [Serializable]
        internal sealed class HeartbeatRsp : IClusterClientMessage, IDeadLetterSuppression
        {
            public static readonly HeartbeatRsp Instance = new HeartbeatRsp();
            private HeartbeatRsp() { }
        }

        [Serializable]
        internal sealed class Ping : IDeadLetterSuppression
        {
            public static readonly Ping Instance = new Ping();
            private Ping() { }
        }

        internal sealed class CheckDeadlines
        {
            public static readonly CheckDeadlines Instance = new CheckDeadlines();
            private CheckDeadlines() { }
        }
        #endregion

        /// <summary>
        /// Factory method for <see cref="ClusterReceptionist"/> <see cref="Actor.Props"/>.
        /// </summary>
        public static Props Props(IActorRef pubSubMediator, ClusterReceptionistSettings settings)
        {
            return Actor.Props.Create(() => new ClusterReceptionist(
                pubSubMediator,
                settings)).WithDeploy(Deploy.Local);
        }


        #region RingOrdering
        internal class RingOrdering : IComparer<Address>
        {
            public static readonly RingOrdering Instance = new RingOrdering();
            private RingOrdering() { }

            public static int HashFor(Address node)
            {
                // cluster node identifier is the host and port of the address; protocol and system is assumed to be the same
                if (!string.IsNullOrEmpty(node.Host) && node.Port.HasValue)
                    return MurmurHash.StringHash(node.Host + ":" + node.Port.Value);
                else
                    throw new IllegalStateException("Unexpected address without host/port: " + node);
            }

            public int Compare(Address a, Address b)
            {
                var ha = HashFor(a);
                var hb = HashFor(b);

                if (ha == hb) return 0;
                return ha < hb || Member.AddressOrdering.Compare(a, b) < 0 ? -1 : 1;
            }
        }
        #endregion

        private ILoggingAdapter _log;
        private readonly IActorRef _pubSubMediator;
        private readonly ClusterReceptionistSettings _settings;
        private readonly Cluster _cluster;
        private ImmutableSortedSet<Address> _nodes;
        private readonly int _virtualNodesFactor;
        private ConsistentHash<Address> _consistentHash;

        private ImmutableDictionary<IActorRef, DeadlineFailureDetector> _clientInteractions;
        private ImmutableHashSet<IActorRef> _clientsPublished;
        private ImmutableList<IActorRef> _subscribers;
        private ICancelable _checkDeadlinesTask;

        public ClusterReceptionist(IActorRef pubSubMediator, ClusterReceptionistSettings settings)
        {
            _log = Context.GetLogger();
            _pubSubMediator = pubSubMediator;
            _settings = settings;
            _cluster = Cluster.Get(Context.System);

            if (!(_settings.Role == null || _cluster.SelfRoles.Contains(_settings.Role)))
            {
                throw new ArgumentException($"This cluster member [{_cluster.SelfAddress}] does not have a role [{_settings.Role}]");
            }

            _nodes = ImmutableSortedSet<Address>.Empty.WithComparer(RingOrdering.Instance);
            _virtualNodesFactor = 10;
            _consistentHash = ConsistentHash.Create(_nodes, _virtualNodesFactor);

            _clientInteractions = ImmutableDictionary<IActorRef, DeadlineFailureDetector>.Empty;
            _clientsPublished = ImmutableHashSet<IActorRef>.Empty;
            _subscribers = ImmutableList<IActorRef>.Empty;
            _checkDeadlinesTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                _settings.FailureDetectionInterval,
                _settings.FailureDetectionInterval,
                Self,
                CheckDeadlines.Instance,
                Self);
        }

        protected override void PreStart()
        {
            base.PreStart();
            if (_cluster.IsTerminated)
            {
                throw new IllegalStateException("Cluster node must not be terminated");
            }
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
            _checkDeadlinesTask.Cancel();
        }

        private bool IsMatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_settings.Role) || member.HasRole(_settings.Role);
        }

        private IActorRef ResponseTunnel(IActorRef client)
        {
            var encName = Uri.EscapeDataString(client.Path.ToSerializationFormat());
            var child = Context.Child(encName);

            return !child.Equals(ActorRefs.Nobody)
                ? child
                : Context.ActorOf(Actor.Props.Create(() => new ClientResponseTunnel(client, _settings.ResponseTunnelReceiveTimeout)), encName);
        }

        protected override bool Receive(object message)
        {
            if (message is PublishSubscribe.Send
                || message is PublishSubscribe.SendToAll
                || message is PublishSubscribe.Publish)
            {
                var tunnel = ResponseTunnel(Sender);
                tunnel.Tell(Ping.Instance); // keep alive
                _pubSubMediator.Tell(message, tunnel);
            }
            else if (message is Heartbeat)
            {
                if (_cluster.Settings.VerboseHeartbeatLogging)
                {
                    _log.Debug("Heartbeat from client [{0}]", Sender.Path);
                }
                Sender.Tell(HeartbeatRsp.Instance);
                UpdateClientInteractions(Sender);
            }
            else if (message is GetContacts)
            {
                // Consistent hashing is used to ensure that the reply to GetContacts
                // is the same from all nodes (most of the time) and it also
                // load balances the client connections among the nodes in the cluster.
                if (_settings.NumberOfContacts >= _nodes.Count)
                {
                    var contacts = new Contacts(_nodes.Select(a => Self.Path.ToStringWithAddress(a)).ToImmutableList());
                    if (_log.IsDebugEnabled)
                        _log.Debug("Client [{0}] gets contactPoints [{1}] (all nodes)", Sender.Path, string.Join(", ", contacts));

                    Sender.Tell(contacts);
                }
                else
                {
                    // using ToStringWithAddress in case the client is local, normally it is not, and
                    // ToStringWithAddress will use the remote address of the client
                    var addr = _consistentHash.NodeFor(Sender.Path.ToStringWithAddress(_cluster.SelfAddress));

                    var first = _nodes.From(addr).Tail().Take(_settings.NumberOfContacts).ToArray();
                    var slice = first.Length == _settings.NumberOfContacts
                        ? first
                        : first.Union(_nodes.Take(_settings.NumberOfContacts - first.Length)).ToArray();
                    var contacts = new Contacts(slice.Select(a => Self.Path.ToStringWithAddress(a)).ToImmutableList());

                    if (_log.IsDebugEnabled)
                        _log.Debug("Client [{0}] gets ContactPoints [{1}]", Sender.Path, string.Join(", ", contacts.ContactPoints));

                    Sender.Tell(contacts);
                    UpdateClientInteractions(Sender);
                }
            }
            else if (message is ClusterEvent.CurrentClusterState)
            {
                var state = (ClusterEvent.CurrentClusterState)message;

                _nodes = ImmutableSortedSet<Address>.Empty.WithComparer(RingOrdering.Instance)
                    .Union(state.Members
                        .Where(m => m.Status != MemberStatus.Joining && IsMatchingRole(m))
                        .Select(m => m.Address));

                _consistentHash = ConsistentHash.Create(_nodes, _virtualNodesFactor);
            }
            else if (message is ClusterEvent.MemberUp)
            {
                var up = (ClusterEvent.MemberUp)message;
                if (IsMatchingRole(up.Member))
                {
                    _nodes = _nodes.Add(up.Member.Address);
                    _consistentHash = ConsistentHash.Create(_nodes, _virtualNodesFactor);
                }
            }
            else if (message is ClusterEvent.MemberRemoved)
            {
                var removed = (ClusterEvent.MemberRemoved)message;

                if (removed.Member.Address.Equals(_cluster.SelfAddress))
                {
                    Context.Stop(Self);
                }
                else if (IsMatchingRole(removed.Member))
                {
                    _nodes = _nodes.Remove(removed.Member.Address);
                    _consistentHash = ConsistentHash.Create(_nodes, _virtualNodesFactor);
                }
            }
            else if (message is ClusterEvent.IMemberEvent)
            {
                // not of interest
            }
            else if (message is SubscribeClusterClients)
            {
                var subscriber = Sender;
                subscriber.Tell(new ClusterClients(_clientInteractions.Keys.ToImmutableHashSet()));
                _subscribers = _subscribers.Add(subscriber);
                Context.Watch(subscriber);
            }
            else if (message is UnsubscribeClusterClients)
            {
                var subscriber = Sender;
                _subscribers = _subscribers.Where(c => !c.Equals(subscriber)).ToImmutableList();
            }
            else if (message is Terminated)
            {
                var terminated = (Terminated)message;
                Self.Tell(UnsubscribeClusterClients.Instance, terminated.ActorRef);
            }
            else if (message is GetClusterClients)
            {
                Sender.Tell(new ClusterClients(_clientInteractions.Keys.ToImmutableHashSet()));
            }
            else if (message is CheckDeadlines)
            {
                _clientInteractions = _clientInteractions.Where(c => c.Value.IsAvailable).ToImmutableDictionary();
                PublishClientsUnreachable();
            }
            else return false;

            return true;
        }

        private void UpdateClientInteractions(IActorRef client)
        {
            if (_clientInteractions.ContainsKey(client))
            {
                var failureDetector = _clientInteractions[client];
                failureDetector.HeartBeat();
            }
            else
            {
                var failureDetector = new DeadlineFailureDetector(_settings.AcceptableHeartbeatPause, _settings.HeartbeatInterval);
                failureDetector.HeartBeat();
                _clientInteractions = _clientInteractions.Add(client, failureDetector);
                _log.Debug($"Received new contact from [{client.Path}]");
                var clusterClientUp = new ClusterClientUp(client);
                _subscribers.ForEach(s => s.Tell(clusterClientUp));
                _clientsPublished = _clientInteractions.Keys.ToImmutableHashSet();
            }
        }

        private void PublishClientsUnreachable()
        {
            var publishableClients = _clientInteractions.Keys.ToImmutableHashSet();
            foreach (var c in _clientsPublished)
            {
                if (!publishableClients.Contains(c))
                {
                    _log.Debug($"Lost contact with [{c.Path}]");
                    var clusterClientUnreachable = new ClusterClientUnreachable(c);
                    _subscribers.ForEach(s => s.Tell(clusterClientUnreachable));
                }
            }
            _clientsPublished = publishableClients;
        }
    }

    /// <summary>
    /// Replies are tunneled via this actor, child of the receptionist, to avoid inbound connections from other cluster nodes to the client.
    /// </summary>
    internal class ClientResponseTunnel : ActorBase
    {
        private readonly IActorRef _client;
        private readonly ILoggingAdapter _log;

        public ClientResponseTunnel(IActorRef client, TimeSpan timeout)
        {
            _client = client;
            _log = Context.GetLogger();
            Context.SetReceiveTimeout(timeout);
        }

        protected override bool Receive(object message)
        {
            if (message is ClusterReceptionist.Ping)
            {
                // keep alive from client
            }
            else if (message is ReceiveTimeout)
            {
                _log.Debug("ClientResponseTunnel for client [{0}] stopped due to inactivity", _client.Path);
                Context.Stop(Self);
            }
            else _client.Tell(message, ActorRefs.NoSender);

            return true;
        }
    }
}
