//-----------------------------------------------------------------------
// <copyright file="ClusterActorRefProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Annotations;
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
    /// INTERNAL API.
    /// 
    /// Marker interface for signifying that this <see cref="IActorRefProvider"/> can be used in combination with the
    /// <see cref="Cluster"/> ActorSystem extension.
    /// </summary>
    [InternalApi]
    public interface IClusterActorRefProvider : IRemoteActorRefProvider { }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The `ClusterActorRefProvider` will load the <see cref="Cluster"/>
    /// extension, i.e. the cluster will automatically be started when
    /// the `ClusterActorRefProvider` is used.
    /// </summary>
    [InternalApi]
    public class ClusterActorRefProvider : RemoteActorRefProvider, IClusterActorRefProvider
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="systemName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="eventStream">TBD</param>
        public ClusterActorRefProvider(string systemName, Settings settings, EventStream eventStream /*DynamicAccess*/)
            : base(systemName, settings, eventStream)
        {
            var clusterConfig = ClusterConfigFactory.Default();
            settings.InjectTopLevelFallback(clusterConfig);
            Deployer = new ClusterDeployer(settings);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public override void Init(ActorSystemImpl system)
        {
            //Complete the usual RemoteActorRefProvider initializations - need access to transports and RemoteWatcher before clustering can work
            base.Init(system);

            // initialize/load the Cluster extension
            Cluster.Get(system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
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
    /// This class represents a binding of an actor deployment to a cluster-aware system.
    /// </summary>
    public class ClusterScope : Scope
    {
        private ClusterScope() { }

        /// <summary>
        /// The singleton instance of this scope.
        /// </summary>
        public static readonly ClusterScope Instance = new ClusterScope();

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Scope" /> from this scope using another <see cref="Akka.Actor.Scope" />
        /// to backfill options that might be missing from this scope.
        ///
        /// <note>
        /// This method ignores the given scope and returns the singleton instance of this scope.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Scope" /> used for fallback configuration.</param>
        /// <returns>The singleton instance of this scope</returns>
        public override Scope WithFallback(Scope other)
        {
            return Instance;
        }

        /// <summary>
        /// Creates a copy of the current instance.
        /// 
        /// <note>
        /// This method returns the singleton instance of this scope.
        /// </note>
        /// </summary>
        /// <returns>The singleton instance of this scope</returns>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterDeployer"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the deployer.</param>
        public ClusterDeployer(Settings settings)
            : base(settings)
        {
        }

        /// <summary>
        /// Creates an actor deployment to the supplied path, <paramref name="key" />, using the supplied configuration, <paramref name="config" />.
        /// </summary>
        /// <param name="key">The path used to deploy the actor.</param>
        /// <param name="config">The configuration used to configure the deployed actor.</param>
        /// <returns>A configured actor deployment to the given path.</returns>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the deployment has a scope defined in the configuration
        /// or the router is configured as a <see cref="RemoteRouterConfig"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the router is not configured as either a <see cref="Pool"/> or a <see cref="Group"/>.
        /// </exception>
        public override Deploy ParseConfig(string key, Config config)
        {
            Config config2 = config;
            if (config.HasPath("cluster.enabled")
                && config.GetBoolean("cluster.enabled", false)
                && !config.HasPath("nr-of-instances"))
            {
                var maxTotalNrOfInstances = config
                    .WithFallback(Default)
                    .GetInt("cluster.max-total-nr-of-instances", 0);
                config2 = ConfigurationFactory.ParseString("nr-of-instances=" + maxTotalNrOfInstances)
                    .WithFallback(config);
            }

            var deploy = base.ParseConfig(key, config2);
            if (deploy != null)
            {
                if (deploy.Config.GetBoolean("cluster.enabled", false))
                {
                    if (deploy.Scope != Deploy.NoScopeGiven)
                        throw new ConfigurationException($"Cluster deployment can't be combined with scope [{deploy.Scope}]");
                    if (deploy.RouterConfig is RemoteRouterConfig)
                        throw new ConfigurationException($"Cluster deployment can't be combined with [{deploy.Config}]");

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
                        throw new ArgumentException($"Cluster-aware router can only wrap Pool or Group, got [{deploy.RouterConfig.GetType()}]");
                    }
                }
                else
                {
                    return deploy;
                }
            }
            else
            {
                return null;
            }
        }
    }
}
