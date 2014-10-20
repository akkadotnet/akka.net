using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.Remote
{
    public class RemoteDeployer : Deployer
    {
        public RemoteDeployer(Settings settings) : base(settings)
        {
        }

        public override Deploy ParseConfig(string key, Config config)
        {
            var deploy = base.ParseConfig(key, config);
            if (deploy == null) return null;

            var remote = deploy.Config.GetString("remote");

            ActorPath actorPath;
            if(ActorPath.TryParse(remote, out actorPath))
            {
                var address = actorPath.Address;
                return deploy.Copy(scope: new RemoteScope(address));
            }
            
            if (!string.IsNullOrWhiteSpace(remote))
                throw new ConfigurationException(string.Format("unparseable remote node name [{0}]", "ARG0"));

            var nodes = deploy.Config.GetStringList("target.nodes").Select(Address.Parse);
            if (nodes.Any() && deploy.RouterConfig != RouterConfig.NoRouter)
            {
                //if(deploy.RouterConfig == RouterConfig.Pool) return deploy.Copy(routerConfig: new RemoteRouterConfig(deploy.RouterConfig, nodes);
                return deploy.Copy(scope: Deploy.NoScopeGiven);
            }
            else
            {
                //TODO: return deploy;
                return deploy.Copy(scope: Deploy.NoScopeGiven);
            }
        }
    }
}
