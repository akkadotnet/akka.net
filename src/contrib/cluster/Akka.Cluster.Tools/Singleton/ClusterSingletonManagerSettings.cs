//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Util;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// The settings used for the <see cref="ClusterSingletonManager"/>
    /// </summary>
    [Serializable]
    public sealed class ClusterSingletonManagerSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Creates a new <see cref="ClusterSingletonManagerSettings"/> instance.
        /// </summary>
        /// <param name="system">The <see cref="ActorSystem"/> to which this singleton manager belongs.</param>
        /// <exception cref="ConfigurationException">Thrown if no "akka.cluster.singleton" section is defined.</exception>
        /// <returns>The requested settings.</returns>
        public static ClusterSingletonManagerSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.singleton");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterSingletonManagerSettings>("akka.cluster.singleton");

            return Create(config).WithRemovalMargin(Cluster.Get(system).DowningProvider.DownRemovalMargin);
        }

        /// <summary>
        /// Creates a new <see cref="ClusterSingletonManagerSettings"/> instance.
        /// </summary>
        /// <param name="config">The HOCON configuration used to create the settings.</param>
        /// <returns>The requested settings.</returns>
        public static ClusterSingletonManagerSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterSingletonManagerSettings>();


            LeaseUsageSettings lease = null;
            var leaseConfigPath = config.GetString("use-lease");
            if (!string.IsNullOrEmpty(leaseConfigPath))
                lease = new LeaseUsageSettings(leaseConfigPath, config.GetTimeSpan("lease-retry-interval"));

            return new ClusterSingletonManagerSettings(
                singletonName: config.GetString("singleton-name"),
                role: RoleOption(config.GetString("role")),
                removalMargin: TimeSpan.Zero, // defaults to ClusterSettings.DownRemovalMargin
                handOverRetryInterval: config.GetTimeSpan("hand-over-retry-interval"),
                leaseSettings: lease);
        }

        private static string RoleOption(string role)
        {
            if (string.IsNullOrEmpty(role))
                return null;
            return role;
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
        /// LeaseSettings for acquiring before creating the singleton actor
        /// </summary>
        public LeaseUsageSettings LeaseSettings { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="ClusterSingletonManagerSettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the child singleton actor.</param>
        /// <param name="role">
        /// Singleton among the nodes tagged with specified role. If the role is not specified
        /// it's a singleton among all nodes in the cluster.
        /// </param>
        /// <param name="removalMargin">
        /// Margin until the singleton instance that belonged to a downed/removed partition is
        /// created in surviving partition. The purpose of  this margin is that in case of
        /// a network partition the singleton actors  in the non-surviving partitions must
        /// be stopped before corresponding actors are started somewhere else.
        /// This is especially important for persistent actors.
        /// </param>
        /// <param name="handOverRetryInterval">
        /// When a node is becoming oldest it sends hand-over
        /// request to previous oldest, that might be leaving the cluster. This is
        /// retried with this interval until the previous oldest confirms that the hand
        /// over has started or the previous oldest member is removed from the cluster
        /// (+ <paramref name="removalMargin"/>).
        /// </param>
        /// <exception cref="ArgumentException">TBD</exception>
        public ClusterSingletonManagerSettings(string singletonName, string role, TimeSpan removalMargin, TimeSpan handOverRetryInterval)
            : this(singletonName, role, removalMargin, handOverRetryInterval, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ClusterSingletonManagerSettings"/>.
        /// </summary>
        /// <param name="singletonName">The actor name of the child singleton actor.</param>
        /// <param name="role">
        /// Singleton among the nodes tagged with specified role. If the role is not specified
        /// it's a singleton among all nodes in the cluster.
        /// </param>
        /// <param name="removalMargin">
        /// Margin until the singleton instance that belonged to a downed/removed partition is
        /// created in surviving partition. The purpose of  this margin is that in case of
        /// a network partition the singleton actors  in the non-surviving partitions must
        /// be stopped before corresponding actors are started somewhere else.
        /// This is especially important for persistent actors.
        /// </param>
        /// <param name="handOverRetryInterval">
        /// When a node is becoming oldest it sends hand-over
        /// request to previous oldest, that might be leaving the cluster. This is
        /// retried with this interval until the previous oldest confirms that the hand
        /// over has started or the previous oldest member is removed from the cluster
        /// (+ <paramref name="removalMargin"/>).
        /// </param>
        /// <param name="leaseSettings">LeaseSettings for acquiring before creating the singleton actor</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public ClusterSingletonManagerSettings(string singletonName, string role, TimeSpan removalMargin, TimeSpan handOverRetryInterval, LeaseUsageSettings leaseSettings)
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
            LeaseSettings = leaseSettings;
        }

        /// <summary>
        /// Create a singleton manager with specified singleton name.
        /// </summary>
        /// <param name="singletonName">TBD</param>
        /// <returns>TBD</returns>
        public ClusterSingletonManagerSettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton role.
        /// </summary>
        /// <param name="role">TBD</param>
        /// <returns>TBD</returns>
        public ClusterSingletonManagerSettings WithRole(string role)
        {
            return Copy(role: RoleOption(role));
        }

        /// <summary>
        /// Create a singleton manager with specified singleton removal margin.
        /// </summary>
        /// <param name="removalMargin">TBD</param>
        /// <returns>TBD</returns>
        public ClusterSingletonManagerSettings WithRemovalMargin(TimeSpan removalMargin)
        {
            return Copy(removalMargin: removalMargin);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton removal margin hand-over retry interval.
        /// </summary>
        /// <param name="handOverRetryInterval">TBD</param>
        /// <returns>TBD</returns>
        public ClusterSingletonManagerSettings WithHandOverRetryInterval(TimeSpan handOverRetryInterval)
        {
            return Copy(handOverRetryInterval: handOverRetryInterval);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton lease settings.
        /// </summary>
        /// <param name="leaseSettings">TBD</param>
        /// <returns></returns>
        public ClusterSingletonManagerSettings WithLeaseSettings(LeaseUsageSettings leaseSettings)
        {
            return Copy(leaseSettings: leaseSettings);
        }

        private ClusterSingletonManagerSettings Copy(string singletonName = null, Option<string> role = default, TimeSpan? removalMargin = null,
            TimeSpan? handOverRetryInterval = null, Option<LeaseUsageSettings> leaseSettings = default)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: singletonName ?? SingletonName,
                role: role.HasValue ? role.Value : Role,
                removalMargin: removalMargin ?? RemovalMargin,
                handOverRetryInterval: handOverRetryInterval ?? HandOverRetryInterval,
                leaseSettings: leaseSettings.HasValue ? leaseSettings.Value : LeaseSettings
                );
        }
    }
}
