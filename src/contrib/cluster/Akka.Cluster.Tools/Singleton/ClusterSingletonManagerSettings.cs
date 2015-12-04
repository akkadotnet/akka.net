//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Singleton
{
    [Serializable]
    public sealed class ClusterSingletonManagerSettings : INoSerializationVerificationNeeded
    {
        public static ClusterSingletonManagerSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton");
            if (config == null)
                throw new ConfigurationException(string.Format("Cannot initialize {0}: akka.cluster.singleton configuration node was not provided", typeof(ClusterSingletonManagerSettings)));

            return Create(config).WithRemovalMargin(Cluster.Get(system).Settings.DownRemovalMargin);
        }

        public static ClusterSingletonManagerSettings Create(Config config)
        {
            var role = config.GetString("role");
            if (role == string.Empty) role = null;
            return new ClusterSingletonManagerSettings(
                singletonName: config.GetString("singleton-name"),
                role: role,
                removalMargin: TimeSpan.MinValue,
                handOverRetryInterval: config.GetTimeSpan("hand-over-retry-interval"));
        }

        public readonly string SingletonName;
        public readonly string Role;
        public readonly TimeSpan RemovalMargin;
        public readonly TimeSpan HandOverRetryInterval;

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
        public ClusterSingletonManagerSettings(string singletonName, string role, TimeSpan removalMargin, TimeSpan handOverRetryInterval)
        {
            if (string.IsNullOrWhiteSpace(singletonName))
                throw new ArgumentNullException("singletonName");
            if (removalMargin == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.RemovalMargin must be positive", "removalMargin");
            if (handOverRetryInterval == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonManagerSettings.HandOverRetryInterval must be positive", "handOverRetryInterval");

            SingletonName = singletonName;
            Role = role;
            RemovalMargin = removalMargin;
            HandOverRetryInterval = handOverRetryInterval;
        }

        public ClusterSingletonManagerSettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        public ClusterSingletonManagerSettings WithRole(string role)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: SingletonName,
                role: role,
                removalMargin: RemovalMargin,
                handOverRetryInterval: HandOverRetryInterval);
        }

        public ClusterSingletonManagerSettings WithRemovalMargin(TimeSpan removalMargin)
        {
            return Copy(removalMargin: removalMargin);
        }

        public ClusterSingletonManagerSettings WithHandOverRetryInterval(TimeSpan handOverRetryInterval)
        {
            return Copy(handOverRetryInterval: handOverRetryInterval);
        }

        private ClusterSingletonManagerSettings Copy(string singletonName = null, string role = null, TimeSpan? removalMargin = null,
            TimeSpan? handOverRetryInterval = null)
        {
            return new ClusterSingletonManagerSettings(
                singletonName: singletonName ?? SingletonName,
                role: role ?? Role,
                removalMargin: removalMargin ?? RemovalMargin,
                handOverRetryInterval: handOverRetryInterval ?? HandOverRetryInterval);
        }
    }
}