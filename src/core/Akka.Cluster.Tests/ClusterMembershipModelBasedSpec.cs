using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    class ClusterMembershipModelBasedSpec
    {
    }

    internal class ClusterMembershipModel
    {
        public ClusterMembershipModel(ImmutableHashSet<Address> allPossibleAddresses, ImmutableHashSet<Address> seedNodes, ImmutableHashSet<UniqueAddress> runningNodes, ImmutableDictionary<Address, MembershipState> currentClusterState)
        {
            AllPossibleAddresses = allPossibleAddresses;
            SeedNodes = seedNodes;
            RunningNodes = runningNodes;
            CurrentClusterState = currentClusterState;
        }

        /// <summary>
        /// Use <see cref="Address"/> here, since the <see cref="UniqueAddress"/> can change during reboots.
        /// </summary>
        public ImmutableHashSet<Address> AllPossibleAddresses { get; }

        /// <summary>
        /// Use <see cref="Address"/> here, since the <see cref="UniqueAddress"/> can change during reboots.
        /// </summary>
        public ImmutableHashSet<Address> SeedNodes { get; }

        /// <summary>
        /// Nodes that are currently "running" inside the cluster
        /// </summary>
        /// <remarks>
        /// All of these nodes must be present inside <see cref="AllPossibleAddresses"/> 
        /// </remarks>
        public ImmutableHashSet<UniqueAddress> RunningNodes { get; }

        /// <summary>
        /// The current state, by node, of each node in the cluster.
        /// </summary>
        /// <remarks>
        /// Can be empty for some members
        /// </remarks>
        public ImmutableDictionary<Address, MembershipState> CurrentClusterState { get; }
    }
}
