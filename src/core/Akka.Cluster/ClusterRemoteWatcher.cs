using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote;

namespace Akka.Cluster
{
    /// <summary>
    /// Specialization of <see cref="Akka.Remote.RemoteWatcher"/> that keeps
    /// track of cluster member nodes and is responsible for watchees on cluster nodes.
    /// <see cref="Akka.Actor.AddressTerminated"/> is published when a node is removed from cluster
    /// 
    /// `RemoteWatcher` handles non-cluster nodes. `ClusterRemoteWatcher` will take
    /// over responsibility from `RemoteWatcher` if a watch is added before a node is member
    /// of the cluster and then later becomes cluster member.
    /// </summary>
    internal class ClusterRemoteWatcher : RemoteWatcher
    {
        /// <summary>
        /// Factory method for <see cref="Akka.Remote.RemoteWatcher"/>
        /// </summary>
        public static Props Props(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter)
        {
            return new Props(typeof(ClusterRemoteWatcher), new object[]
            {
                failureDetector, 
                heartbeatInterval, 
                unreachableReaperInterval, 
                heartbeatExpectedResponseAfter
            }).WithDeploy(Deploy.Local);
        }

        readonly Cluster _cluster;
        ImmutableHashSet<Address> _clusterNodes = ImmutableHashSet.Create<Address>();

        public ClusterRemoteWatcher(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter) :base(failureDetector, heartbeatInterval, unreachableReaperInterval, heartbeatExpectedResponseAfter)
        {
            _cluster = Cluster.Get(Context.System);
        }

        protected override void PreStart()
        {
            base.PreStart();
            _cluster.Subscribe(Self, new []{typeof(ClusterEvent.IMemberEvent)});
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            var watchRemote = message as WatchRemote;
            if (watchRemote != null && _clusterNodes.Contains(watchRemote.Watchee.Path.Address))
                return; // cluster managed node, don't propagate to super;
            var state = message as ClusterEvent.CurrentClusterState;
            if (state != null)
            {
                _clusterNodes =
                    state.Members.Select(m => m.Address).Where(a => a != _cluster.SelfAddress).ToImmutableHashSet();
                foreach(var node in _clusterNodes) TakeOverResponsibility(node);
                Unreachable.ExceptWith(_clusterNodes);
                return;
            }
            var memberUp = message as ClusterEvent.MemberUp;
            if (memberUp != null)
            {
                if (memberUp.Member.Address != _cluster.SelfAddress)
                {
                    _clusterNodes = _clusterNodes.Add(memberUp.Member.Address);
                    TakeOverResponsibility(memberUp.Member.Address);
                    Unreachable.Remove(memberUp.Member.Address);
                }
                return;
            }
            var memberRemoved = message as ClusterEvent.MemberRemoved;
            if (memberRemoved != null)
            {
                if (memberRemoved.Member.Address != _cluster.SelfAddress)
                {
                    _clusterNodes = _clusterNodes.Remove(memberRemoved.Member.Address);
                    if (memberRemoved.PreviousStatus == MemberStatus.Down)
                    {
                        Quarantine(memberRemoved.Member.Address, memberRemoved.Member.UniqueAddress.Uid);
                    }
                    PublishAddressTerminated(memberRemoved.Member.Address);
                }
                return;
            }
            if (message is ClusterEvent.IMemberEvent) return; // not interesting
            base.OnReceive(message);
        }

        /// <summary>
        /// When a cluster node is added this class takes over the
        /// responsibility for watchees on that node already handled
        /// by base RemoteWatcher.
        /// </summary>
        private void TakeOverResponsibility(Address address)
        {
            foreach (var watching in Watching)
            {
                var watchee = watching.Item1;
                var watcher = watching.Item2;
                if (watchee.Path.Address.Equals(address))
                    ProcessUnwatchRemote(watchee, watcher);
            }
        }
    }
}
