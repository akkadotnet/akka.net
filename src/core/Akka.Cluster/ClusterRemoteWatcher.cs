//-----------------------------------------------------------------------
// <copyright file="ClusterRemoteWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        /// <param name="failureDetector">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="unreachableReaperInterval">TBD</param>
        /// <param name="heartbeatExpectedResponseAfter">TBD</param>
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

        private readonly Cluster _cluster;
        private ImmutableHashSet<Address> _clusterNodes = ImmutableHashSet.Create<Address>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="failureDetector">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="unreachableReaperInterval">TBD</param>
        /// <param name="heartbeatExpectedResponseAfter">TBD</param>
        public ClusterRemoteWatcher(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter) :base(failureDetector, heartbeatInterval, unreachableReaperInterval, heartbeatExpectedResponseAfter)
        {
            _cluster = Cluster.Get(Context.System);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            base.PreStart();
            _cluster.Subscribe(Self, new []{typeof(ClusterEvent.IMemberEvent)});
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    _clusterNodes =
                        state.Members.Select(m => m.Address).Where(a => a != _cluster.SelfAddress).ToImmutableHashSet();
                    foreach (var node in _clusterNodes) TakeOverResponsibility(node);
                    Unreachable.ExceptWith(_clusterNodes);
                    return;
                case ClusterEvent.MemberUp up:
                    MemberUp(up.Member);
                    return;
                case ClusterEvent.MemberWeaklyUp weaklyUp:
                    MemberUp(weaklyUp.Member);
                    return;
                case ClusterEvent.MemberRemoved removed:
                    MemberRemoved(removed.Member, removed.PreviousStatus);
                    return;
                case ClusterEvent.IMemberEvent _:
                    return; // not interesting
            }

            base.OnReceive(message);
        }

        private void MemberUp(Member member)
        {
            if (!member.Address.Equals(_cluster.SelfAddress))
            {
                _clusterNodes = _clusterNodes.Add(member.Address);
                TakeOverResponsibility(member.Address);
                Unreachable.Remove(member.Address);
            }
        }

        private void MemberRemoved(Member member, MemberStatus previousStatus)
        {
            if (!member.Address.Equals(_cluster.SelfAddress))
            {
                _clusterNodes = _clusterNodes.Remove(member.Address);
                if (previousStatus == MemberStatus.Down)
                {
                    Quarantine(member.Address, member.UniqueAddress.Uid);
                }
                PublishAddressTerminated(member.Address);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
        protected override void WatchNode(IInternalActorRef watchee)
        {
            if (!_clusterNodes.Contains(watchee.Path.Address)) base.WatchNode(watchee);
        }

        /// <summary>
        /// When a cluster node is added this class takes over the
        /// responsibility for watchees on that node already handled
        /// by base RemoteWatcher.
        /// </summary>
        private void TakeOverResponsibility(Address address)
        {
            if (WatchingNodes.Contains(address))
            {
                Log.Debug("Cluster is taking over responsibility of node: {0}", address);
                UnwatchNode(address);
            }
        }
    }
}

