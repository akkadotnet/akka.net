//-----------------------------------------------------------------------
// <copyright file="RemoteDeployer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Used for deployment of actors on remote systems
    /// </summary>
    internal class RemoteDeployer : Deployer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteDeployer"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the deployer.</param>
        public RemoteDeployer(Settings settings) : base(settings)
        {
        }

        /// <summary>
        /// Creates an actor deployment to the supplied path, <paramref name="key" />, using the supplied configuration, <paramref name="config" />.
        /// </summary>
        /// <param name="key">The path used to deploy the actor.</param>
        /// <param name="config">The configuration used to configure the deployed actor.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when a remote node name in the specified <paramref name="config"/> is unparseable.
        /// </exception>
        /// <returns>A configured actor deployment to the given path.</returns>
        public override Deploy ParseConfig(string key, Config config)
        {
            var deploy = base.ParseConfig(key, config);
            if (deploy == null) return null;

            var remote = deploy.Config.GetString("remote", null);

            ActorPath actorPath;
            if(ActorPath.TryParse(remote, out actorPath))
            {
                var address = actorPath.Address;
                //can have remotely deployed routers that remotely deploy routees
                return CheckRemoteRouterConfig(deploy.WithScope(scope: new RemoteScope(address)));
            }
            
            if (!string.IsNullOrWhiteSpace(remote))
                throw new ConfigurationException($"unparseable remote node name [{remote}]");

            return CheckRemoteRouterConfig(deploy);
        }

        private static Deploy CheckRemoteRouterConfig(Deploy deploy)
        {
            var nodes = deploy.Config.GetStringList("target.nodes", new string[] { }).Select(Address.Parse).ToList();
            if (nodes.Any() && deploy.RouterConfig != null)
            {
                if (deploy.RouterConfig is Pool)
                    return
                        deploy.WithRouterConfig(new RemoteRouterConfig(deploy.RouterConfig.AsInstanceOf<Pool>(), nodes));
                return deploy.WithScope(scope: Deploy.NoScopeGiven);
            }
            else
            {
                //TODO: return deploy;
                return deploy;
            }
        }
    }
}
