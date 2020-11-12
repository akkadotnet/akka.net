//-----------------------------------------------------------------------
// <copyright file="DDataRememberEntitiesProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Sharding.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class DDataRememberEntitiesProvider : IRememberEntitiesProvider
    {
        public DDataRememberEntitiesProvider(
            string typeName,
            ClusterShardingSettings settings,
            int majorityMinCap,
            IActorRef replicator)
        {
            TypeName = typeName;
            Settings = settings;
            MajorityMinCap = majorityMinCap;
            Replicator = replicator;
        }

        public string TypeName { get; }
        public ClusterShardingSettings Settings { get; }
        public int MajorityMinCap { get; }
        public IActorRef Replicator { get; }

        public Props CoordinatorStoreProps()
        {
            return DDataRememberEntitiesCoordinatorStore.Props(TypeName, Settings, Replicator, MajorityMinCap);
        }

        public Props ShardStoreProps(string shardId)
        {
            return DDataRememberEntitiesShardStore.Props(shardId, TypeName, Settings, Replicator, MajorityMinCap);
        }
    }
}
