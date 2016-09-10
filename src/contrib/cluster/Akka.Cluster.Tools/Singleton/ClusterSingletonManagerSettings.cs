//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Singleton
{
    public sealed class ClusterSingletonManagerSettings : INoSerializationVerificationNeeded
    {
        public static ClusterSingletonManagerSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.singleton");
            if (config == null)
                throw new ConfigurationException(string.Format("Cannot initialize {0}: akka.cluster.singleton configuration node was not provided", typeof(ClusterSingletonManagerSettings)));

            return Create(config).WithRemovalMargin(Cluster.Get(system).Settings.DownRemovalMargin);
        }

        public static ClusterSingletonManagerSettings Create(Config config)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: config.GetString("singleton-name"),
                role: RoleOption(config.GetString("role")),
                removalMargin: TimeSpan.Zero, // defaults to ClusterSettins.DownRemovalMargin
                handOverRetryInterval: config.GetTimeSpan("hand-over-retry-interval"));
        }

        private static string RoleOption(string role)
        {
            if (String.IsNullOrEmpty(role))
                return null;
            return role;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ClusterSingletonManagerSettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the child singleton actor.</param>
        /// <param name="role">Singleton among the nodes tagged with specified role.</param>
        /// <param name="removalMargin">Margin until the singleton instance that belonged to a downed/removed partition is created in surviving partition.</param>
        /// <param name="handOverRetryInterval">When a node is becoming oldest it sends hand-over request to previous oldest, that might be leaving the cluster.</param>
        public ClusterSingletonManagerSettings(string singletonName, string role, TimeSpan removalMargin, TimeSpan handOverRetryInterval)
        {
            if (string.IsNullOrWhiteSpace(singletonName))
                throw new ArgumentNullException(nameof(singletonName));
            if (removalMargin < TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.RemovalMargin must be positive", nameof(removalMargin));
            if (handOverRetryInterval <= TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.HandOverRetryInterval must be positive", nameof(handOverRetryInterval));

            SingletonName = singletonName;
            Role = role;
            RemovalMargin = removalMargin;
            HandOverRetryInterval = handOverRetryInterval;
        }

        /// <summary>
        /// The actor name of the child singleton actor.
        /// </summary>
        public string SingletonName { get; }

        /// <summary>
        /// Singleton among the nodes tagged with specified role.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// Margin until the singleton instance that belonged to a downed/removed partition is created in surviving partition.
        /// </summary>
        public TimeSpan RemovalMargin { get; }

        /// <summary>
        /// When a node is becoming oldest it sends hand-over request to previous oldest, that might be leaving the cluster.
        /// </summary>
        public TimeSpan HandOverRetryInterval { get; }

        /// <summary>
        /// Create a singleton manager with specified singleton name.
        /// </summary>
        public ClusterSingletonManagerSettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton role.
        /// </summary>
        public ClusterSingletonManagerSettings WithRole(string role)
        {
            return Copy(role: RoleOption(role));
        }

        /// <summary>
        /// Create a singleton manager with specified singleton remova margin.
        /// </summary>
        public ClusterSingletonManagerSettings WithRemovalMargin(TimeSpan removalMargin)
        {
            return Copy(removalMargin: removalMargin);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton remova margin hand-over retry interval.
        /// </summary>
        public ClusterSingletonManagerSettings WithHandOverRetryInterval(TimeSpan handOverRetryInterval)
        {
            return Copy(handOverRetryInterval: handOverRetryInterval);
        }

        private ClusterSingletonManagerSettings Copy(string singletonName = null, string role = null, TimeSpan? removalMargin = null, TimeSpan? handOverRetryInterval = null)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: singletonName ?? SingletonName,
                role: role ?? Role,
                removalMargin: removalMargin ?? RemovalMargin,
                handOverRetryInterval: handOverRetryInterval ?? HandOverRetryInterval);
        }
    }
}