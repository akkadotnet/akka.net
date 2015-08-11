using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using Akka.IO;
using Akka.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class Replicator : ActorBase
    {
        static readonly ByteString DeletedDigest = ByteString.Empty;
        static readonly ByteString LazyDigest = ByteString.Create(new byte[] { 0 }, 0, 1);
        static readonly ByteString NotFoundDigest = ByteString.Create(new byte[] { 255 }, 0, 1);

        readonly ReplicatorSettings _settings;

        readonly Cluster.Cluster _cluster;
        readonly Address _selfAddress;
        readonly UniqueAddress _selfUniqueAddress;

        readonly ICancelable _gossipTask;
        readonly ICancelable _notifyTask;
        readonly ICancelable _pruningTask;
        readonly ICancelable _clockTask;

        readonly Serializer _serializer;
        readonly long _maxPruningDisseminationNanos;

        IImmutableSet<Address> _nodes;

        IImmutableDictionary<UniqueAddress, long> _removedNodes;
        IImmutableDictionary<UniqueAddress, long> _pruningPerformed;
        IImmutableSet<UniqueAddress> _tombstonedNodes;

        Address _leader = null;

        long _previousClockTime;
        long _allReachableClockTime = 0;
        IImmutableSet<Address> _unreachable;

        IImmutableDictionary<string, Tuple<DataEnvelope, ByteString>> _dataEntries;
        IImmutableSet<string> _changed;

        long _statusCount = 0;
        int _statusTotChunks = 0;

        readonly Dictionary<string, HashSet<IActorRef>> _subscribers = new Dictionary<string, HashSet<IActorRef>>();
        readonly Dictionary<string, HashSet<IActorRef>> _newSubscribers = new Dictionary<string, HashSet<IActorRef>>();
        IImmutableDictionary<string, KeyR> _subscriptionKeys;

        public Replicator(ReplicatorSettings settings)
        {
            _settings = settings;
            _cluster = Cluster.Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;
            _selfUniqueAddress = _cluster.SelfUniqueAddress;

            if(_cluster.IsTerminated)
            {
                throw new ArgumentException("Cluster node must not be terminated");
            }
            if(_settings.Role != null && !_cluster.SelfRoles.Contains(_settings.Role))
            {
                throw new ArgumentException(String.Format("The cluster node {0} does not have the role {1}", _selfAddress, _settings.Role));
            }

            _gossipTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, new object(), Self);
            _notifyTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.NotifySubscribersInterval, _settings.NotifySubscribersInterval, Self, new object(), Self);
            _pruningTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.PruningInterval, _settings.PruningInterval, Self, new object(), Self);
            _clockTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, new object(), Self);

            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DataEnvelope));
            _maxPruningDisseminationNanos = (long)_settings.MaxPruningDissemination.Ticks * 100;

            _nodes = ImmutableHashSet<Address>.Empty;

            _removedNodes = ImmutableDictionary<UniqueAddress, long>.Empty;
            _pruningPerformed = ImmutableDictionary<UniqueAddress, long>.Empty;
            _tombstonedNodes = ImmutableHashSet<UniqueAddress>.Empty;

            _previousClockTime = Context.System.Scheduler.MonotonicClock.Ticks * 100;
            _unreachable = ImmutableHashSet<Address>.Empty;

            _dataEntries = ImmutableDictionary<string, Tuple<DataEnvelope, ByteString>>.Empty;
            _changed = ImmutableHashSet<string>.Empty;

            _subscriptionKeys = ImmutableDictionary<string, KeyR>.Empty;
        }

        protected override void PreStart()
        {
            var leaderChangedClass = _settings.Role != null ? typeof(ClusterEvent.RoleLeaderChanged) : typeof(ClusterEvent.LeaderChanged);
            _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, new[]{ typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent), leaderChangedClass });
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            _gossipTask.Cancel();
            _notifyTask.Cancel();
            _pruningTask.Cancel();
            _clockTask.Cancel();
        }

        protected override bool Receive(object message)
        {
            return message.Match()
                          .With<Get<IReplicatedData>>(g => ReceiveGet(g.Key, g.Consistency, g.Request))
                          .With<Update<IReplicatedData>>(u => ReceiveUpdate(u.Key, u.Modify, u.Consistency, u.Request))
                          .With<Read>(r => ReceiveRead(r.Key))
                          .With<Write>(w => ReceiveWrite(w.Key, w.Envelope))
                          .With<ReadRepair>(rr => ReceiveReadRepair(rr.Key, rr.Envelope))
                          .With<FlushChanges>(x => ReceiveFlushChanges())
                          .With<GossipTick>(_ => ReceiveGossipTick())
                          .With<ClockTick>(c => ReceiveClockTick())
                          .With<Akka.DistributedData.Internal.Status>(s => ReceiveStatus(s.Digests, s.Chunk, s.TotChunks))
                          .With<Gossip>(g => ReceiveGossip(g.UpdatedData, g.SendBack))
                          .With<Subscribe<IReplicatedData>>(s => ReceiveSubscribe(s.Key, s.Subscriber))
                          .With<Unsubscribe<IReplicatedData>>(u => ReceiveUnsubscribe(u.Key, u.Subscriber))
                          .With<Terminated>(t => ReceiveTerminated(t.ActorRef))
                          .With<ClusterEvent.MemberUp>(m => ReceiveMemberUp(m.Member))
                          .With<ClusterEvent.MemberRemoved>(m => ReceiveMemberRemoved(m.Member))
                          .With<ClusterEvent.IMemberEvent>(_ => { })
                          .With<ClusterEvent.UnreachableMember>(u => ReceiveUnreachable(u.Member))
                          .With<ClusterEvent.ReachableMember>(r => ReceiveReachable(r.Member))
                          .With<ClusterEvent.LeaderChanged>(l => ReceiveLeaderChanged(l.Leader, null))
                          .With<ClusterEvent.RoleLeaderChanged>(r => ReceiveLeaderChanged(r.Leader, r.Role))
                          .With<GetKeyIds>(_ => ReceiveGetKeyIds())
                          .With<Delete<IReplicatedData>>(d => ReceiveDelete(d.Key, d.Consistency))
                          .With<RemovedNodePruningTick>(r => ReceiveRemovedNodePruningTick())
                          .With<GetReplicaCount>(_ => ReceiveGetReplicaCount())
                          .WasHandled;
        }

        private void ReceiveGet(Key<IReplicatedData> key, IReadConsistency consistency, Object req)
        {
            var localValue = GetData(key.Id);
            Context.System.Log.Debug("Received get for key {0}, local value {1}", key.Id, localValue);
            if(IsLocalGet(consistency))
            {

            }
        }

        private bool IsLocalGet(IReadConsistency consistency)
        {
            if(consistency is ReadLocal)
            {
                return true;
            }
            if(consistency is ReadAll || consistency is ReadMajority)
            {
                return _nodes.Count == 0;
            }
            return false;
        }

        private void ReceiveRead(string key)
        {

        }

        private bool MatchingRole(Member m)
        {
            return _settings.Role != null && m.HasRole(_settings.Role);
        }

        private bool IsLocalSender()
        {
            return !Sender.Path.Address.HasGlobalScope;
        }

        private void ReceiveUpdate(Key<IReplicatedData> key, Func<IReplicatedData, IReplicatedData> modify, IWriteConsistency consistency, object request)
        {

        }

        private bool IsLocalUpdate(IWriteConsistency consistency)
        {
            if(consistency is WriteLocal)
            {
                return true;
            }
            if(consistency is WriteAll || consistency is WriteMajority)
            {
                return _nodes.Count == 0;
            }
            return false;
        }

        private void ReceiveWrite(string key, DataEnvelope envelope)
        {

        }

        private void Write(string key, DataEnvelope writeEnvelope)
        {

        }

        private void ReceiveReadRepair(string key, DataEnvelope writeEnvelope)
        {

        }

        private void ReceiveGetKeyIds()
        {

        }

        private void ReceiveDelete(Key<IReplicatedData> key, IWriteConsistency consistency)
        {

        }

        private void SetData(string key, DataEnvelope envelope)
        {

        }

        private ByteString Digest(DataEnvelope envelope)
        {

        }

        private DataEnvelope GetData(string key)
        {

        }

        private void ReceiveFlushChanges()
        {

        }

        private void ReceiveGossipTick()
        {

        }

        private void GossipTo(Address address)
        {

        }

        private Address SelectRandomNode(IEnumerable<Address> addresses)
        {

        }

        private ActorSelection Replica(Address address)
        {
            return Context.ActorSelection(Self.Path.ToStringWithAddress(address));
        }

        private void ReceiveStatus(IImmutableDictionary<string, ByteString> otherDigests, int chunk, int totChunks)
        {

        }

        private void ReceiveGossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack)
        {

        }

        private void ReceiveSubscribe(Key<IReplicatedData> key, IActorRef subscriber)
        {

        }

        private void ReceiveUnsubscribe(Key<IReplicatedData> key, IActorRef subscriber)
        {

        }

        private bool HasSubscriber(IActorRef subscriber)
        {
            return _subscribers.Any(kvp => kvp.Value.Contains(subscriber)) ||
                _newSubscribers.Any(kvp => kvp.Value.Contains(subscriber));
        }

        private void ReceiveTerminated(IActorRef terminated)
        {

        }

        private void ReceiveMemberUp(Member m)
        {
            if(MatchingRole(m) && m.Address != _selfAddress)
            {
                _nodes = _nodes.Add(m.Address);
            }
        }

        private void ReceiveMemberRemoved(Member m)
        {
            if(m.Address == _selfAddress)
            {
                Context.Stop(Self);
            }
            else if(MatchingRole(m))
            {
                _nodes = _nodes.Remove(m.Address);
                _removedNodes = _removedNodes.SetItem(m.UniqueAddress, _allReachableClockTime);
                _unreachable = _unreachable.Remove(m.Address);
            }
        }

        private void ReceiveUnreachable(Member m)
        {
            if(MatchingRole(m))
            {
                _unreachable = _unreachable.Add(m.Address);
            }
        }

        private void ReceiveReachable(Member m)
        {
            if(MatchingRole(m))
            {
                _unreachable = _unreachable.Remove(m.Address);
            }
        }

        private void ReceiveLeaderChanged(Address leader, string role)
        {
            if(role == _settings.Role)
            {
                _leader = leader;
            }
        }

        private void ReceiveClockTick()
        {

        }

        private void ReceiveRemovedNodePruningTick()
        {

        }

        private void ReceiveGetReplicaCount()
        {
            Sender.Tell(new ReplicaCount(_nodes.Count + 1));
        }
    }
}
