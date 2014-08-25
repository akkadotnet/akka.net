using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{
    internal class ClusterReadView : IDisposable
    {
        //TODO: Volatiles?
        /// <summary>
        /// Current state
        /// </summary>
        ClusterEvent.CurrentClusterState _state;

        public ClusterEvent.CurrentClusterState State { get { return _state; } }

        Reachability _reachability;

        /// <summary>
        /// Current internal cluster stats, updated periodically via event bus.
        /// </summary>
        ClusterEvent.CurrentInternalStats _latestStats;

        /// <summary>
        /// Current cluster metrics, updated periodically via event bus.
        /// </summary>
        ImmutableHashSet<NodeMetrics> _clusterMetrics;

        readonly Address _selfAddress;

        public Address SelfAddress
        {
            get { return _selfAddress; }
        }

        readonly ActorRef _eventBusListener;

        public ClusterReadView(Cluster cluster)
        {
            _state = new ClusterEvent.CurrentClusterState();
            _reachability = Reachability.Empty;
            _latestStats = new ClusterEvent.CurrentInternalStats(new GossipStats(), new VectorClockStats());
            _clusterMetrics = ImmutableHashSet.Create<NodeMetrics>();
            _selfAddress = cluster.SelfAddress;


        }

        private class EventBusListener : UntypedActor
        {
            readonly Cluster _cluster;

            public EventBusListener(Cluster cluster)
            {
                _cluster = cluster;
            }

            protected override void PreStart()
            {
                throw new NotImplementedException();
            }

            protected override void OnReceive(object message)
            {
                throw new NotImplementedException();
            }
        }
 


        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
