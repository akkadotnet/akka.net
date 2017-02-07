﻿//-----------------------------------------------------------------------
// <copyright file="RemoteDeployer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
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
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        public RemoteDeployer(Settings settings) : base(settings)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="config">TBD</param>
        /// <exception cref="ConfigurationException">TBD</exception>
        /// <returns>TBD</returns>
        public override Deploy ParseConfig(string key, Config config)
        {
            var deploy = base.ParseConfig(key, config);
            if (deploy == null) return null;

            var remote = deploy.Config.GetString("remote");

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
            var nodes = deploy.Config.GetStringList("target.nodes").Select(Address.Parse).ToList();
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

