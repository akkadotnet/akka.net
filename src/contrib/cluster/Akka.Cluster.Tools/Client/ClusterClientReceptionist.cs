using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client
{

    /**
     * Extension that starts [[ClusterReceptionist]] and accompanying [[akka.cluster.pubsub.DistributedPubSubMediator]]
     * with settings defined in config section `akka.cluster.client.receptionist`.
     * The [[akka.cluster.pubsub.DistributedPubSubMediator]] is started by the [[akka.cluster.pubsub.DistributedPubSub]] extension.
     */
    public class ClusterClientReceptionist : ExtensionIdProvider<ClusterClientReceptionist>, IExtension
    {
        private readonly Config _config;
        private readonly string _role;

        private ClusterClientReceptionist(ExtendedActorSystem system)
        {
            _config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            _role = _config.GetString("role");
        }

        public override ClusterClientReceptionist CreateExtension(ExtendedActorSystem system)
        {
            return new ClusterClientReceptionist(system);
        }
    }
}