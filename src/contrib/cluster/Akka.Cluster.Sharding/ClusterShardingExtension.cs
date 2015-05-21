using Akka.Actor;

namespace Akka.Cluster.Sharding
{
    public class ClusterShardingExtension : ExtensionIdProvider<ClusterSharding>
    {
        public override ClusterSharding CreateExtension(ExtendedActorSystem system)
        {
            var extension = new ClusterSharding(system);
            return extension;
        }
    }
}
