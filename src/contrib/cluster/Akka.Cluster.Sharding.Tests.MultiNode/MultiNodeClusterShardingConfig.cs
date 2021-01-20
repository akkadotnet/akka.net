//-----------------------------------------------------------------------
// <copyright file="MultiNodeClusterShardingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class MultiNodeClusterShardingConfig : MultiNodeConfig
    {
        protected static Config PersistenceConfig()
        {
            return ConfigurationFactory.ParseString($@"""
                akka.actor {{
                    serializers {{
                        hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                    }}
                    serialization-bindings {{
                        ""System.Object"" = hyperion
                    }}
                }}
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""

                akka.persistence.journal.MemoryJournal {{
                    class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                }}
                akka.persistence.journal.memory-journal-shared {{
                    class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    timeout = 5s
                }}
                """);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="mode">mode the state store mode</param>
        /// <param name="rememberEntities">rememberEntities defaults to off</param>
        /// <param name="additionalConfig">additionalConfig additional config</param>
        /// <param name="loglevel">loglevel defaults to INFO</param>
        protected MultiNodeClusterShardingConfig(
            string mode = ClusterShardingSettings.StateStoreModeDData,
            bool rememberEntities = false,
            string additionalConfig = "",
            string loglevel = "INFO")
        {
            Mode = mode;
            RememberEntities = rememberEntities;
            AdditionalConfig = additionalConfig;
            Loglevel = loglevel;

            Config persistenceConfig = (mode == ClusterShardingSettings.StateStoreModeDData) ? ConfigurationFactory.Empty : PersistenceConfig();

            Config common =
                ConfigurationFactory
                    .ParseString($@"""
                        akka.actor.provider = ""cluster""
                        akka.cluster.testkit.auto-down-unreachable-after = 0s
                        akka.cluster.sharding.state-store-mode = ""{mode}""
                        akka.cluster.sharding.remember-entities = {rememberEntities}
                        akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                        akka.loglevel = {loglevel}
                        akka.remote.log-remote-lifecycle-events = off

                        akka.cluster.sharding {{
                            updating-state-timeout = 2s
                            waiting-for-state-timeout = 2s
                        }}
                        """)
                    .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                    .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                    .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            CommonConfig = (ConfigurationFactory.ParseString(additionalConfig).WithFallback(persistenceConfig).WithFallback(common));
        }

        public string Mode { get; }
        public bool RememberEntities { get; }
        public string AdditionalConfig { get; }
        public string Loglevel { get; }
    }
}
