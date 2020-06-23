//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;

namespace Akka.Cluster.Sharding
{
    [Serializable]
    [ApiMayChange]
    public sealed class ShardedDaemonProcessSettings
    {
        /// <summary>
        /// The interval each parent of the sharded set is pinged from each node in the cluster.
        /// </summary>
        public readonly TimeSpan KeepAliveInterval;

        /// <summary>
        /// Specify sharding settings that should be used for the sharded daemon process instead of loading from config.
        /// </summary>
        public readonly ClusterShardingSettings ShardingSettings;

        /// <summary>
        /// Create default settings for system
        /// </summary>
        public static ShardedDaemonProcessSettings Create(ActorSystem system)
        {
            return FromConfig(system.Settings.Config.GetConfig("akka.cluster.sharded-daemon-process"));
        }

        public static ShardedDaemonProcessSettings FromConfig(Config config)
        {
            var keepAliveInterval = config.GetTimeSpan("keep-alive-interval");
            return new ShardedDaemonProcessSettings(keepAliveInterval);
        }

        /// <summary>
        /// Not for user constructions, use factory methods to instantiate.
        /// </summary>
        private ShardedDaemonProcessSettings(TimeSpan keepAliveInterval, ClusterShardingSettings shardingSettings = null)
        {
            KeepAliveInterval = keepAliveInterval;
            ShardingSettings = shardingSettings;
        }

        /// <summary>
        /// NOTE: How the sharded set is kept alive may change in the future meaning this setting may go away.
        /// </summary>
        /// <param name="keepAliveInterval">The interval each parent of the sharded set is pinged from each node in the cluster.</param>
        public ShardedDaemonProcessSettings WithKeepAliveInterval(TimeSpan keepAliveInterval)
        {
            return new ShardedDaemonProcessSettings(keepAliveInterval, ShardingSettings);
        }

        /// <summary>
        /// Specify sharding settings that should be used for the sharded daemon process instead of loading from config.
        /// Some settings can not be changed (remember-entities and related settings, passivation, number-of-shards),
        /// changing those settings will be ignored.
        /// </summary>
        /// <param name="shardingSettings">TBD</param>
        public ShardedDaemonProcessSettings WithShardingSettings(ClusterShardingSettings shardingSettings)
        {
            return new ShardedDaemonProcessSettings(KeepAliveInterval, shardingSettings);
        }
    }
}
