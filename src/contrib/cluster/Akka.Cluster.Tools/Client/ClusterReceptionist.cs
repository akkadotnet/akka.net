//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionist.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Pattern;
using Akka.Routing;
using Akka.Util;

namespace Akka.Cluster.Tools.Client
{
    public interface IClusterClientMessage { }

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
    /// the <see cref="Sender"/>, as seen by the destination actor, is not the client itself.
    /// The <see cref="Sender"/> of the response messages, as seen by the client, is preserved
    /// as the original sender, so the client can choose to send subsequent messages
    /// directly to the actor in the cluster.
    /// </para>
    /// </summary>
    public class ClusterReceptionist : ActorBase
    {
        #region Messages

        [Serializable]
        internal sealed class GetContacts : IClusterClientMessage
        {
            public static readonly GetContacts Instance = new GetContacts();
            private GetContacts() { }
        }

        [Serializable]
        internal sealed class Contacts : IClusterClientMessage
        {
            public readonly string[] ContactPoints;

            public Contacts(string[] contactPoints)
            {
                ContactPoints = contactPoints ?? new string[0];
            }
        }

        [Serializable]
        internal sealed class Heartbeat : IClusterClientMessage
        {
            public static readonly Heartbeat Instance = new Heartbeat();
            private Heartbeat() { }
        }

        [Serializable]
        internal sealed class HeartbeatRsp : IClusterClientMessage
        {
            public static readonly HeartbeatRsp Instance = new HeartbeatRsp();
            private HeartbeatRsp() { }
        }

        [Serializable]
        internal sealed class Ping
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

        /// <summary>
        /// Factory method for <see cref="ClusterReceptionist"/> <see cref="Actor.Props"/>.
        /// </summary>
        public static Actor.Props Props(IActorRef mediator, ClusterReceptionistSettings settings)
        {
            return Actor.Props.Create(() => new ClusterReceptionist(mediator, settings)).WithDeploy(Deploy.Local);
        }

        private const int VirtualNodesFactor = 10;
        private readonly IActorRef _pubSubMediator;
        private readonly ClusterReceptionistSettings _settings;
        private readonly Cluster _cluster = Cluster.Get(Context.System);

        private ConsistentHash<Address> _consistentHash;
        private SortedDictionary<int, Address> _nodes;
        private ILoggingAdapter _log;

        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        public ClusterReceptionist(IActorRef pubSubMediator, ClusterReceptionistSettings settings)
        {
            _pubSubMediator = pubSubMediator;
            _settings = settings;

            if (!(_settings.Role == null || _cluster.SelfRoles.Contains(_settings.Role)))
                throw new ArgumentException(string.Format("This cluster member [{0}] does not have a role [{1}]", _cluster.SelfAddress, _settings.Role));

            _nodes = GetNodes();
            _consistentHash = new ConsistentHash<Address>(_nodes, VirtualNodesFactor);
        }

        protected override bool Receive(object message)
        {
            if (message is Send || message is SendToAll || message is Publish)
            {
                var tunnel = ResponseTunnel(Sender);
                tunnel.Tell(Ping.Instance);
                _pubSubMediator.Tell(message, tunnel);
            }
            else if (message is Heartbeat)
            {
                Log.Debug("Heartbeat from client [{0}]", Sender.Path);
                Sender.Tell(HeartbeatRsp.Instance);
            }
            else if (message is GetContacts)
            {
                // Consistent hashing is used to ensure that the reply to GetContacts
                // is the same from all nodes (most of the time) and it also
                // load balances the client connections among the nodes in the cluster.
                if (_settings.NumberOfContacts > _nodes.Count)
                {
                    var contacts = _nodes.Select(kv => Self.Path.ToStringWithAddress(kv.Value)).ToArray();
                    if (Log.IsDebugEnabled)
                        Log.Debug("Client [{0}] gets contactPoints [{1}] (all nodes)", Sender.Path, string.Join(", ", contacts));

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
                    var contacts = new Contacts(slice.Select(a => Self.Path.ToStringWithAddress(a.Value)).ToArray());

                    if (Log.IsDebugEnabled)
                        Log.Debug("Client [{0}] gets contactPoints [{1}]", Sender.Path, string.Join(", ", contacts.ContactPoints));

                    Sender.Tell(contacts);
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
                _consistentHash = new ConsistentHash<Address>(_nodes, VirtualNodesFactor);

            }
            else if (message is ClusterEvent.MemberUp)
            {
                var up = (ClusterEvent.MemberUp)message;
                if (IsMatchingRole(up.Member))
                {
                    _nodes.Add(RingOrdering.HashFor(up.Member.Address), up.Member.Address);
                    _consistentHash = new ConsistentHash<Address>(_nodes, VirtualNodesFactor);
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
                    _consistentHash = new ConsistentHash<Address>(_nodes, VirtualNodesFactor);
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
            var encName = Uri.EscapeDataString(client.Path.ToSerializationFormat());
            var child = Context.Child(encName);

            return !Equals(child, ActorRefs.Nobody)
                ? child
                : Context.ActorOf(Actor.Props.Create(() => new ClientResponseTunnel(client, _settings.ResponseTunnelReceiveTimeout)), encName);
        }
    }

    /// <summary>
    /// Replies are tunneled via this actor, child of the receptionist, to avoid inbound connections from other cluster nodes to the client.
    /// </summary>
    internal class ClientResponseTunnel : UntypedActor
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
            else _client.Tell(message, ActorRefs.NoSender);
        }
    }
}