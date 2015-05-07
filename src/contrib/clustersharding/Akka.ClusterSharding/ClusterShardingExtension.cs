using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.ClusterSharding
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
