using Akka.Actor;
using Akka.Cluster;
using Akka.IO;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    using Akka.Serialization;
using Digest = ByteString;

    public class Replicator : ActorBase
    {
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

        IImmutableDictionary<string, Tuple<DataEnvelope, Digest>> _dataEntries;
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

            _dataEntries = ImmutableDictionary<string, Tuple<DataEnvelope, Digest>>.Empty;
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
            throw new NotImplementedException();
        }

        private bool MatchingRole(Member m)
        {
            return _settings.Role != null && m.HasRole(_settings.Role);
        }
    }
}
