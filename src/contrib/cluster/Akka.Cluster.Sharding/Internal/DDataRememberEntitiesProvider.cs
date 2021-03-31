using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Cluster.Sharding.Internal
{
    internal class DDataRememberEntitiesProvider : IRememberEntitiesProvider
    {
        private readonly string _typeName;
        private readonly ClusterShardingSettings _settings;
        private readonly int _majorityMinCap;
        private readonly IActorRef _replicator;

        public DDataRememberEntitiesProvider(
            string typeName, 
            ClusterShardingSettings settings, 
            int majorityMinCap, 
            IActorRef replicator)
        {
            _typeName = typeName;
            _settings = settings;
            _majorityMinCap = majorityMinCap;
            _replicator = replicator;
        }

        public Props CoordinatorStoreProps()
            => DDataRememberEntitiesCoordinatorStore.Props(_typeName, _settings, _replicator, _majorityMinCap);

        public Props ShardStoreProps(string shardId)
        {
            throw new NotImplementedException();
        }
    }
}
