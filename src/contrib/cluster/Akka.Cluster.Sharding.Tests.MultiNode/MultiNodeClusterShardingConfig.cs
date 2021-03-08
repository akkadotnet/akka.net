//-----------------------------------------------------------------------
// <copyright file="MultiNodeClusterShardingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class MultiNodeClusterShardingConfig : MultiNodeConfig
    {
        protected static Config PersistenceConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""
                akka.persistence.journal.memory-journal-shared {
                    class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    timeout = 5s
                }

                akka.persistence.snapshot-store.plugin = ""akka.persistence.memory-snapshot-store-shared""
                akka.persistence.memory-snapshot-store-shared {
                    class = ""Akka.Cluster.Sharding.Tests.MemorySnapshotStoreShared, Akka.Cluster.Sharding.Tests.MultiNode""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    timeout = 5s
                }
                ");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="mode">mode the state store mode</param>
        /// <param name="rememberEntities">rememberEntities defaults to off</param>
        /// <param name="additionalConfig">additionalConfig additional config</param>
        /// <param name="loglevel">loglevel defaults to INFO</param>
        protected MultiNodeClusterShardingConfig(
            StateStoreMode mode = StateStoreMode.DData,
            bool rememberEntities = false,
            RememberEntitiesStore rememberEntitiesStore = RememberEntitiesStore.DData,
            string additionalConfig = "",
            string loglevel = "INFO")
        {
            Mode = mode;
            RememberEntities = rememberEntities;
            RememberEntitiesStore = rememberEntitiesStore;
            AdditionalConfig = additionalConfig;
            Loglevel = loglevel;

            Config persistenceConfig = (mode == StateStoreMode.DData && rememberEntitiesStore != RememberEntitiesStore.Eventsourced) ? ConfigurationFactory.Empty : PersistenceConfig();

            Common =
                ConfigurationFactory.ParseString($@"
                    akka.actor.provider = ""cluster""
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.sharding.state-store-mode = ""{mode}""
                    akka.cluster.sharding.remember-entities = {rememberEntities.ToString().ToLowerInvariant()}
                    akka.cluster.sharding.remember-entities-store = ""{rememberEntitiesStore}""

                    akka.cluster.sharding.verbose-debug-logging = on

                    akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                    akka.loglevel = {loglevel}
                    akka.remote.log-remote-lifecycle-events = off

                    akka.actor {{
                        serializers {{
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = hyperion
                        }}
                    }}
                    ")
                    .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                    .WithFallback(DistributedData.DistributedData.DefaultConfig())
                    .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                    .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            CommonConfig = (ConfigurationFactory.ParseString(additionalConfig).WithFallback(persistenceConfig).WithFallback(Common));
        }

        public StateStoreMode Mode { get; }
        public bool RememberEntities { get; }
        public RememberEntitiesStore RememberEntitiesStore { get; }
        public string AdditionalConfig { get; }
        public string Loglevel { get; }
        public Config Common { get; }
    }
}
