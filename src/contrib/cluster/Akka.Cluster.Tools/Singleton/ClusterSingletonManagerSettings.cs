//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            system.Settings.InjectTopLevelFallback(ClusterSingleton.DefaultConfig());

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
                leaseSettings: lease,
                considerAppVersion: config.GetBoolean("consider-app-version"));
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
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </summary>
        [Obsolete("ConsiderAppVersion is not used anymore and will be removed in future versions.")]
        public bool ConsiderAppVersion { get; }

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
        /// <param name="considerAppVersion">
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="singletonName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="removalMargin"/> is negative or <paramref name="handOverRetryInterval"/> is zero or negative.</exception>
        public ClusterSingletonManagerSettings(
            string singletonName,
            string role,
            TimeSpan removalMargin,
            TimeSpan handOverRetryInterval,
            bool considerAppVersion)
            : this(singletonName, role, removalMargin, handOverRetryInterval, null, considerAppVersion)
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
        /// <param name="considerAppVersion">
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="singletonName"/> is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="removalMargin"/> is negative or <paramref name="handOverRetryInterval"/> is zero or negative.</exception>
        public ClusterSingletonManagerSettings(
            string singletonName,
            string role,
            TimeSpan removalMargin,
            TimeSpan handOverRetryInterval,
            LeaseUsageSettings leaseSettings,
            bool considerAppVersion)
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
#pragma warning disable CS0618 // Type or member is obsolete
            ConsiderAppVersion = considerAppVersion;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Create a singleton manager with specified singleton name.
        /// </summary>
        /// <param name="singletonName">The actor name to use for the child singleton actor.</param>
        /// <returns>A copy of these settings with the specified singleton actor name.</returns>
        public ClusterSingletonManagerSettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton role.
        /// </summary>
        /// <param name="role">The cluster role whose members may host the singleton; null or empty leaves the current role unchanged.</param>
        /// <returns>A copy of these settings with the role changed when a nonempty role is supplied.</returns>
        public ClusterSingletonManagerSettings WithRole(string role)
        {
            return Copy(role: RoleOption(role));
        }

        /// <summary>
        /// Create a singleton manager with specified singleton removal margin.
        /// </summary>
        /// <param name="removalMargin">The delay after removal of an old member before creating the singleton on the surviving oldest member.</param>
        /// <returns>A copy of these settings with the specified removal margin.</returns>
        public ClusterSingletonManagerSettings WithRemovalMargin(TimeSpan removalMargin)
        {
            return Copy(removalMargin: removalMargin);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton removal margin hand-over retry interval.
        /// </summary>
        /// <param name="handOverRetryInterval">The interval between retries while requesting hand-over from the previous oldest member.</param>
        /// <returns>A copy of these settings with the specified hand-over retry interval.</returns>
        public ClusterSingletonManagerSettings WithHandOverRetryInterval(TimeSpan handOverRetryInterval)
        {
            return Copy(handOverRetryInterval: handOverRetryInterval);
        }

        /// <summary>
        /// Create a singleton manager with specified singleton lease settings.
        /// </summary>
        /// <param name="leaseSettings">The lease settings used to acquire a lease before starting the singleton; null leaves the current lease settings unchanged.</param>
        /// <returns>A copy of these settings with the lease settings changed when non-null settings are supplied.</returns>
        public ClusterSingletonManagerSettings WithLeaseSettings(LeaseUsageSettings leaseSettings)
        {
            return Copy(leaseSettings: leaseSettings);
        }

        private ClusterSingletonManagerSettings Copy(
            string singletonName = null,
            Option<string> role = default,
            TimeSpan? removalMargin = null,
            TimeSpan? handOverRetryInterval = null,
            Option<LeaseUsageSettings> leaseSettings = default,
            bool? considerAppVersion = null)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: singletonName ?? SingletonName,
                role: role.HasValue ? role.Value : Role,
                removalMargin: removalMargin ?? RemovalMargin,
                handOverRetryInterval: handOverRetryInterval ?? HandOverRetryInterval,
                leaseSettings: leaseSettings.HasValue ? leaseSettings.Value : LeaseSettings,
                considerAppVersion: false
                );
        }
    }
}
