using System;
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
    internal class ClusterRemoteWatcher
    {
        public ClusterRemoteWatcher(
            IFailureDetectorRegistry<Address> failureDetector,
            TimeSpan heartbeatInterval,
            TimeSpan unreachableReaperInterval,
            TimeSpan heartbeatExpectedResponseAfter)
        {
            throw new NotImplementedException();
        }
    }
}
