//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Util;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// The settings used for the <see cref="ClusterSingleton"/>
    /// </summary>
    [Serializable]
    public class ClusterSingletonSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Singleton among the nodes tagged with specified role. If the role is not specified it's a singleton among all nodes in the cluster.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// Interval at which the proxy will try to resolve the singleton instance.
        /// </summary>
        public TimeSpan SingletonIdentificationInterval { get; }

        /// <summary>
        /// Margin until the singleton instance that belonged to a downed/removed partition is created in surviving partition. 
        /// The purpose of this margin is that in case of a network partition the singleton actors in the non-surviving 
        /// partitions must be stopped before corresponding actors are started somewhere else. This is especially important 
        /// for persistent actors.
        /// </summary>
        public TimeSpan RemovalMargin { get; }

        /// <summary>
        /// When a node is becoming oldest it sends hand-over request to previous oldest, that might be leaving the cluster.
        /// This is retried with this interval until the previous oldest confirms that the hand over has started or the 
        /// previous oldest member is removed from the cluster (+ `removalMargin`).
        /// </summary>
        public TimeSpan HandOverRetryInterval { get; }

        /// <summary>
        /// If the location of the singleton is unknown the proxy will buffer this number of messages and deliver them when the singleton 
        /// is identified. When the buffer is full old messages will be dropped when new messages are sent viea the proxy. Use `0` to 
        /// disable buffering, i.e. messages will be dropped immediately if the location of the singleton is unknown.
        /// </summary>
        public int BufferSize { get; }

        /// <summary>
        /// LeaseSettings for acquiring before creating the singleton actor.
        /// </summary>
        public LeaseUsageSettings LeaseSettings { get; }
        
        /// <summary>
        /// Should <see cref="Member.AppVersion"/> be considered when the cluster singleton instance is being moved to another node.
        /// When set to false, singleton instance will always be created on oldest member.
        /// When set to true, singleton instance will be created on the oldest member with the highest <see cref="Member.AppVersion"/> number.
        /// </summary>
        public bool ConsiderAppVersion { get; }

        /// <summary>
        /// Create settings from the default configuration `akka.cluster`.
        /// </summary>
        public static ClusterSingletonSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());
            return Create(system.Settings.Config.GetConfig("akka.cluster"));
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration `akka.cluster.singleton` and `akka.cluster.singleton-proxy`.
        /// </summary>
        public static ClusterSingletonSettings Create(Config config)
        {
            var mgrSettings = ClusterSingletonManagerSettings.Create(config.GetConfig("singleton"));
            var proxySettings = ClusterSingletonProxySettings.Create(config.GetConfig("singleton-proxy"), false);

            return new ClusterSingletonSettings(
                mgrSettings.Role,
                proxySettings.SingletonIdentificationInterval,
                mgrSettings.RemovalMargin,
                mgrSettings.HandOverRetryInterval,
                proxySettings.BufferSize,
                mgrSettings.LeaseSettings,
                mgrSettings.ConsiderAppVersion);
        }

        private ClusterSingletonSettings(
            string role,
            TimeSpan singletonIdentificationInterval,
            TimeSpan removalMargin,
            TimeSpan handOverRetryInterval,
            int bufferSize,
            LeaseUsageSettings leaseSettings,
            bool considerAppVersion)
        {
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("singletonIdentificationInterval must be positive", nameof(singletonIdentificationInterval));

            if (removalMargin < TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.RemovalMargin must be positive", nameof(removalMargin));

            if (handOverRetryInterval <= TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.HandOverRetryInterval must be positive", nameof(handOverRetryInterval));

            if (bufferSize < 0 || bufferSize > 10000)
                throw new ArgumentException("bufferSize must be >= 0 and <= 10000", nameof(bufferSize));

            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            RemovalMargin = removalMargin;
            HandOverRetryInterval = handOverRetryInterval;
            BufferSize = bufferSize;
            LeaseSettings = leaseSettings;
            ConsiderAppVersion = considerAppVersion;
        }

        public ClusterSingletonSettings WithRole(string role) => Copy(role: role);

        public ClusterSingletonSettings WithRemovalMargin(TimeSpan removalMargin) => Copy(removalMargin: removalMargin);

        public ClusterSingletonSettings WithHandOverRetryInterval(TimeSpan handOverRetryInterval) => Copy(handOverRetryInterval: handOverRetryInterval);

        public ClusterSingletonSettings WithLeaseSettings(LeaseUsageSettings leaseSettings) => Copy(leaseSettings: leaseSettings);

        private ClusterSingletonSettings Copy(
            Option<string> role = default,
            TimeSpan? singletonIdentificationInterval = null,
            TimeSpan? removalMargin = null,
            TimeSpan? handOverRetryInterval = null,
            int? bufferSize = null,
            Option<LeaseUsageSettings> leaseSettings = default,
            bool? considerAppVersion = null)
        {
            return new ClusterSingletonSettings(
                role: role.HasValue ? role.Value : Role,
                singletonIdentificationInterval: singletonIdentificationInterval ?? SingletonIdentificationInterval,
                removalMargin: removalMargin ?? RemovalMargin,
                handOverRetryInterval: handOverRetryInterval ?? HandOverRetryInterval,
                bufferSize: bufferSize ?? BufferSize,
                leaseSettings: leaseSettings.HasValue ? leaseSettings.Value : LeaseSettings,
                considerAppVersion: considerAppVersion ?? ConsiderAppVersion);
        }

        [InternalApi]
        internal ClusterSingletonManagerSettings ToManagerSettings(string singletonName) =>
            new ClusterSingletonManagerSettings(singletonName, Role, RemovalMargin, HandOverRetryInterval, LeaseSettings, ConsiderAppVersion);

        [InternalApi]
        internal ClusterSingletonProxySettings ToProxySettings(string singletonName) =>
            new ClusterSingletonProxySettings(singletonName, Role, SingletonIdentificationInterval, BufferSize, ConsiderAppVersion);

        [InternalApi]
        internal bool ShouldRunManager(Cluster cluster) => string.IsNullOrEmpty(Role) || cluster.SelfMember.Roles.Contains(Role);

        public override string ToString() =>
            $"ClusterSingletonSettings({Role}, {SingletonIdentificationInterval}, {RemovalMargin}, {HandOverRetryInterval}, {BufferSize}, {LeaseSettings}, {ConsiderAppVersion})";
    }
}
