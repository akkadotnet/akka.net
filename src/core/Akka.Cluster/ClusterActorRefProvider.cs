//-----------------------------------------------------------------------
// <copyright file="ClusterActorRefProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Cluster.Configuration;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Remote.Routing;
using Akka.Routing;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// The `ClusterActorRefProvider` will load the <see cref="Cluster"/>
    /// extension, i.e. the cluster will automatically be started when
    /// the `ClusterActorRefProvider` is used.
    /// </summary>
    public class ClusterActorRefProvider : RemoteActorRefProvider
    {
        public ClusterActorRefProvider(string systemName, Settings settings, EventStream eventStream /*DynamicAccess*/)
            : base(systemName, settings, eventStream)
        {
            var clusterConfig = ClusterConfigFactory.Default();
            settings.InjectTopLevelFallback(clusterConfig);
            Deployer = new ClusterDeployer(settings);
        }

        public override void Init(ActorSystemImpl system)
        {
            //Complete the usual RemoteActorRefProvider initializations - need access to transports and RemoteWatcher before clustering can work
            base.Init(system);

            // initialize/load the Cluster extension
            Cluster.Get(system);
        }

        protected override IActorRef CreateRemoteWatcher(ActorSystemImpl system)
        {
            // make sure Cluster extension is initialized/loaded from init thread
            Cluster.Get(system);

            var failureDetector = CreateRemoteWatcherFailureDetector(system);
            return system.SystemActorOf(ClusterRemoteWatcher.Props(
                failureDetector,
                RemoteSettings.WatchHeartBeatInterval,
                RemoteSettings.WatchUnreachableReaperInterval,
                RemoteSettings.WatchHeartbeatExpectedResponseAfter), "remote-watcher");
        }

        
    }

    /// <summary>
    /// Cluster-aware scope of a <see cref="Deploy"/>
    /// </summary>
    public class ClusterScope : Scope
    {
        private ClusterScope() { }

        public static readonly ClusterScope Instance = new ClusterScope();
        public override Scope WithFallback(Scope other)
        {
            return Instance;
        }

        public override Scope Copy()
        {
            return Instance;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Deployer of cluster-aware routers
    /// </summary>
    internal class ClusterDeployer : RemoteDeployer
    {
        public ClusterDeployer(Settings settings)
            : base(settings)
        {
        }

        public override Deploy ParseConfig(string key, Config config)
        {
            var deploy = base.ParseConfig(key, config);
            if (deploy == null) return null;

            if (deploy.Config.GetBoolean("cluster.enabled"))
            {
                if(deploy.Scope != Deploy.NoScopeGiven)
                    throw new ConfigurationException(string.Format("Cluster deployment can't be combined with scope [{0}]", deploy.Scope));
                if(deploy.RouterConfig is RemoteRouterConfig)
                    throw new ConfigurationException(string.Format("Cluster deployment can't be combined with [{0}]", deploy.Config));

                if (deploy.RouterConfig is Pool)
                {
                    return
                        deploy.WithScope(scope: ClusterScope.Instance)
                            .WithRouterConfig(new ClusterRouterPool(deploy.RouterConfig as Pool,
                                ClusterRouterPoolSettings.FromConfig(deploy.Config)));
                }
                else if (deploy.RouterConfig is Group)
                {
                    return
                        deploy.WithScope(scope: ClusterScope.Instance)
                            .WithRouterConfig(new ClusterRouterGroup(deploy.RouterConfig as Group,
                                ClusterRouterGroupSettings.FromConfig(deploy.Config)));
                }
                else
                {
                    throw new ArgumentException(string.Format("Cluster-aware router can only wrap Pool or Group, got [{0}]", deploy.RouterConfig.GetType()));
                }
            }
            else
            {
                return deploy;
            }
        }
    }
}
