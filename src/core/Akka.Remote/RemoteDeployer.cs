using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote
{
    public class RemoteDeployer : Deployer
    {
        public RemoteDeployer(Settings settings) : base(settings)
        {

        }

        protected override Scope ParseScope(Config config)
        {
            var remote = config.GetString("remote");
            if (remote == null)
                return Deploy.NoScopeGiven;

            ActorPath actorPath;
            if(ActorPath.TryParse(remote, out actorPath))
            {
                var address = actorPath.Address;
                return new RemoteScope(address);
            }

            throw new ConfigurationException(string.Format("unparseable remote node name [{0}]", "ARG0"));  
        }
    }
}
