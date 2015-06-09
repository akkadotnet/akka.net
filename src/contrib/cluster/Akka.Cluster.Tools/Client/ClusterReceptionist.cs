using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Routing;
using Akka.Util;

namespace Akka.Cluster.Tools.Client
{

    public class ClusterReceptionistSettings
    {
        public static ClusterReceptionistSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            if (config == null)
                throw new ArgumentException(string.Format("Actor system [{0}] doesn't have `akka.cluster.client.receptionist` config set up", system.Name));

            return new ClusterReceptionistSettings(config.GetString("role"), config.GetInt("number-of-contacts"), config.GetTimeSpan("response-tunnel-receive-timeout"));
        }

        public readonly string Role;
        public readonly int NumberOfContacts;
        public readonly TimeSpan ResponseTunnelReceiveTimeout;

        /**
         * @param role Start the receptionist on members tagged with this role.
         *   All members are used if undefined.
         * @param numberOfContacts The receptionist will send this number of contact points to the client
         * @param responseTunnelReceiveTimeout The actor that tunnel response messages to the
         *   client will be stopped after this time of inactivity.
         */
        public ClusterReceptionistSettings(string role, int numberOfContacts, TimeSpan responseTunnelReceiveTimeout)
        {
            Role = !string.IsNullOrEmpty(role) ? role : null;
            NumberOfContacts = numberOfContacts;
            ResponseTunnelReceiveTimeout = responseTunnelReceiveTimeout;
        }

        public ClusterReceptionistSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        public ClusterReceptionistSettings WithoutRole()
        {
            return Copy(role: null);
        }

        public ClusterReceptionistSettings WithNumberOfContacts(int numberOfContacts)
        {
            return Copy(numberOfContacts: numberOfContacts);
        }

        public ClusterReceptionistSettings WithResponseTunnelReceiveTimeout(TimeSpan responseTunnelReceiveTimeout)
        {
            return Copy(responseTunnelReceiveTimeout: responseTunnelReceiveTimeout);
        }

        private ClusterReceptionistSettings Copy(string role = null, int? numberOfContacts = null,
            TimeSpan? responseTunnelReceiveTimeout = null)
        {
            return new ClusterReceptionistSettings(role ?? Role, numberOfContacts ?? NumberOfContacts, responseTunnelReceiveTimeout ?? ResponseTunnelReceiveTimeout);
        }
    }

    /**
     * [[ClusterClient]] connects to this actor to retrieve. The `ClusterReceptionist` is
     * supposed to be started on all nodes, or all nodes with specified role, in the cluster.
     * The receptionist can be started with the [[ClusterClientReceptionist]] or as an
     * ordinary actor (use the factory method [[ClusterReceptionist#props]]).
     *
     * The receptionist forwards messages from the client to the associated [[akka.cluster.pubsub.DistributedPubSubMediator]],
     * i.e. the client can send messages to any actor in the cluster that is registered in the
     * `DistributedPubSubMediator`. Messages from the client are wrapped in
     * [[akka.cluster.pubsub.DistributedPubSubMediator.Send]], [[akka.cluster.pubsub.DistributedPubSubMediator.SendToAll]]
     * or [[akka.cluster.pubsub.DistributedPubSubMediator.Publish]] with the semantics described in
     * [[akka.cluster.pubsub.DistributedPubSubMediator]].
     *
     * Response messages from the destination actor are tunneled via the receptionist
     * to avoid inbound connections from other cluster nodes to the client, i.e.
     * the `sender`, as seen by the destination actor, is not the client itself.
     * The `sender` of the response messages, as seen by the client, is preserved
     * as the original sender, so the client can choose to send subsequent messages
     * directly to the actor in the cluster.
     */
    public class ClusterReceptionist : ActorBase
    {
        #region Messages

        [Serializable]
        public sealed class GetContacts
        {
            public static readonly GetContacts Instance = new GetContacts();
            private GetContacts() { }
        }

        [Serializable]
        public sealed class Contacts
        {
            public readonly ActorSelection[] ContactPoints;

            public Contacts(ActorSelection[] contactPoints)
            {
                ContactPoints = contactPoints;
            }
        }

        [Serializable]
        public sealed class Heartbeat
        {
            public static readonly Heartbeat Instance = new Heartbeat();
            private Heartbeat() { }
        }

        [Serializable]
        public sealed class HeartbeatRsp
        {
            public static readonly HeartbeatRsp Instance = new HeartbeatRsp();
            private HeartbeatRsp() { }
        }

        [Serializable]
        public sealed class Ping
        {
            public static readonly Ping Instance = new Ping();
            private Ping() { }
        }

        #endregion

        internal class RingOrdering : IComparer<Address>
        {
            public static readonly RingOrdering Instance = new RingOrdering();
            private RingOrdering() { }

            public static int HashFor(Address node)
            {
                // cluster node identifier is the host and port of the address; protocol and system is assumed to be the same
                if (!string.IsNullOrEmpty(node.Host) && node.Port.HasValue)
                    return MurmurHash.StringHash(node.Host + ":" + node.Port.Value.ToString());
                else
                    throw new IllegalStateException("Unexpected address without host/port: " + node);
            }

            public int Compare(Address x, Address y)
            {
                var hx = HashFor(x);
                var hy = HashFor(y);

                if (hx == hy) return 0;
                return hx < hy || Member.AddressOrdering.Compare(x, y) < 0 ? -1 : 1;
            }
        }

        private readonly IActorRef _pubSubMediator;
        private readonly ClusterReceptionistSettings _settings;
        private readonly int _virtualNodesFactor = 10;

        private ConsistentHash<Address> _consistentHash;
        private Cluster _cluster;
        private ILoggingAdapter _log;
        private SortedDictionary<int, Address> _nodes;

        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        public ClusterReceptionist(IActorRef pubSubMediator, ClusterReceptionistSettings settings)
        {
            _pubSubMediator = pubSubMediator;
            _settings = settings;
            _cluster = Cluster.Get(Context.System);

            if (!(_settings.Role == null || _cluster.SelfRoles.Contains(_settings.Role)))
                throw new ArgumentException(string.Format("This cluster member [{0}] does not have a role [{1}]", _cluster.SelfAddress, _settings.Role));

            _nodes = GetNodes();
            _consistentHash = new ConsistentHash<Address>(_nodes, _virtualNodesFactor);
        }

        protected override bool Receive(object message)
        {
            if (message is ClusterClient.Send || message is ClusterClient.SendToAll || message is ClusterClient.Publish)
            {
                var tunnel = ResponseTunnel(Sender);
                tunnel.Tell(Ping.Instance);
                _pubSubMediator.Tell(message, tunnel);
            }
            else if (message is Heartbeat)
            {
                Sender.Tell(HeartbeatRsp.Instance);
            }
            else if (message is GetContacts)
            {
                // Consistent hashing is used to ensure that the reply to GetContacts
                // is the same from all nodes (most of the time) and it also
                // load balances the client connections among the nodes in the cluster.
                if (_settings.NumberOfContacts > _nodes.Count)
                {
                    var contacts = _nodes.Select(kv => Context.ActorSelection(Self.Path.ToStringWithAddress(kv.Value))).ToArray();
                    Sender.Tell(new Contacts(contacts));
                }
                else
                {
                    // using toStringWithAddress in case the client is local, normally it is not, and
                    // toStringWithAddress will use the remote address of the client

                    var addr = _consistentHash.NodeFor(Sender.Path.ToStringWithAddress(_cluster.SelfAddress));
                    //TODO: this should be changed to some kind of _nodes.GetGreaterThan(addr)
                    var first = _nodes.Where(a => RingOrdering.Instance.Compare(a.Value, addr) == 1).Take(_settings.NumberOfContacts).ToArray();
                    var slice = first.Length == _settings.NumberOfContacts
                        ? first
                        : first.Union(_nodes.Take(_settings.NumberOfContacts - first.Length)).ToArray();

                    Sender.Tell(new Contacts(slice.Select(a => Context.ActorSelection(Self.Path.ToStringWithAddress(a.Value))).ToArray()));
                }
            }
            else if (message is ClusterEvent.CurrentClusterState)
            {
                var state = (ClusterEvent.CurrentClusterState)message;

                _nodes = GetNodes();
                foreach (var member in state.Members.Where(m => m.Status != MemberStatus.Joining && IsMatchingRole(m)))
                {
                    _nodes.Add(RingOrdering.HashFor(member.Address), member.Address);
                }
                _consistentHash = new ConsistentHash<Address>(_nodes, _virtualNodesFactor);

            }
            else if (message is ClusterEvent.MemberUp)
            {
                var up = (ClusterEvent.MemberUp)message;
                if (IsMatchingRole(up.Member))
                {
                    _nodes.Add(RingOrdering.HashFor(up.Member.Address), up.Member.Address);
                    _consistentHash = new ConsistentHash<Address>(_nodes, _virtualNodesFactor);
                }
            }
            else if (message is ClusterEvent.MemberRemoved)
            {
                var removed = (ClusterEvent.MemberRemoved)message;
                if (removed.Member.Address == _cluster.SelfAddress)
                {
                    Context.Stop(Self);
                }
                else if (IsMatchingRole(removed.Member))
                {
                    _nodes.Remove(RingOrdering.HashFor(removed.Member.Address));
                    _consistentHash = new ConsistentHash<Address>(_nodes, _virtualNodesFactor);
                }
            }
            else if (message is ClusterEvent.IMemberEvent)
            {
                /* ignore */
            }
            else return false;
            return true;
        }

        protected override void PreStart()
        {
            base.PreStart();
            if (_cluster.IsTerminated) throw new IllegalStateException("Cluster node must not be terminated");
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
        }

        private SortedDictionary<int, Address> GetNodes()
        {
            return new SortedDictionary<int, Address>();
        }

        private bool IsMatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_settings.Role) || member.HasRole(_settings.Role);
        }

        private IActorRef ResponseTunnel(IActorRef client)
        {
            // val encName = URLEncoder.encode(client.path.toSerializationFormat, "utf-8")
            var encName = client.Path.ToSerializationFormat();
            var child = Context.Child(encName);

            return child != ActorRefs.Nobody
                ? child
                : Context.ActorOf(Props.Create(() => new ClientResponseTunnel(client, _settings.ResponseTunnelReceiveTimeout)), encName);
        }
    }

    /**
     * Replies are tunneled via this actor, child of the receptionist, to avoid
     * inbound connections from other cluster nodes to the client.
     */
    public class ClientResponseTunnel : UntypedActor
    {
        private readonly IActorRef _client;

        public ClientResponseTunnel(IActorRef client, TimeSpan timeout)
        {
            _client = client;
            Context.SetReceiveTimeout(timeout);
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterReceptionist.Ping) { /* ignore */ }
            else if (message is ReceiveTimeout) Context.Stop(Self);
            else _client.Forward(message);
        }
    }
}