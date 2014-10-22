﻿using System;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Cluster.Configuration;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
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
        public ClusterActorRefProvider(string systemName, Settings settings, EventStream eventStream /*DynamicAcccess*/)
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

        protected override ActorRef CreateRemoteWatcher(ActorSystem system)
        {
            // make sure Cluster extension is initialized/loaded from init thread
            Cluster.Get(system);

            var failureDetector = CreateRemoteWatcherFailureDetector(system);
            return system.ActorOf(ClusterRemoteWatcher.Props(
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
                //TODO: add handling for RemoteRouterConfig

                if (deploy.RouterConfig is Pool)
                {
                    return
                        deploy.Copy(scope: ClusterScope.Instance)
                            .WithRouterConfig(new ClusterRouterPool(deploy.RouterConfig as Pool,
                                ClusterRouterPoolSettings.FromConfig(deploy.Config)));
                }
                else if (deploy.RouterConfig is Group)
                {
                    return
                        deploy.Copy(scope: ClusterScope.Instance)
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
