using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class DistributedData : IExtension
    {
        readonly Config _config;
        readonly ReplicatorSettings _settings;
        readonly ActorSystem _system;
        readonly IActorRef _replicator;

        public bool IsTerminated
        {
            get { return Cluster.Cluster.Get(_system).IsTerminated || (_settings.Role != null && Cluster.Cluster.Get(_system).SelfRoles.Contains(_settings.Role)); }
        }

        public IActorRef GetReplicator
        {
            get { return _replicator; }
        }

        public DistributedData(ExtendedActorSystem system)
        {
            _config = system.Settings.Config.GetConfig("akka.cluster.distributed-data");
            _settings = new ReplicatorSettings(_config);
            _system = system;
            if(IsTerminated)
            {
                system.Log.Warning("Replicator points to dead letters: Make sure the cluster node is not terminated and has the proper role!");
                _replicator = system.DeadLetters;
            }
            else
            {
                var name = _config.GetString("name");
                _replicator = system.ActorOf(Replicator.GetProps(_settings), name);
            }
        }
    }

    public class DistributedDataExtension : ExtensionIdProvider<DistributedData>
    {
        public override DistributedData CreateExtension(ExtendedActorSystem system)
        {
            return new DistributedData(system);
        }
    }

}
