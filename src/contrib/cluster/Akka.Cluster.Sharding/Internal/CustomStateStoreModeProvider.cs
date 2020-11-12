//-----------------------------------------------------------------------
// <copyright file="CustomStateStoreModeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding.Internal
{
    using ShardId = String;

    /// <summary>
    /// Only intended for testing, not an extension point.
    /// </summary>
    internal sealed class CustomStateStoreModeProvider : IRememberEntitiesProvider
    {
        private readonly ILoggingAdapter log;
        private readonly IRememberEntitiesProvider customStore;

        public CustomStateStoreModeProvider(
                string typeName,
                ActorSystem system,
                ClusterShardingSettings settings)
        {
            log = Logging.GetLogger(system, "CustomStateStoreModeProvider");

            log.Warning("Using custom remember entities store for [{0}], not intended for production use.", typeName);

            if (system.Settings.Config.HasPath("akka.cluster.sharding.remember-entities-custom-store"))
            {
                var customClassName = system.Settings.Config.GetString("akka.cluster.sharding.remember-entities-custom-store");
                var type = Type.GetType(customClassName, true);
                customStore = (IRememberEntitiesProvider)Activator.CreateInstance(type, settings, typeName);

                log.Debug("Will use custom remember entities store provider [{0}]", customStore);
            }
            else
            {
                log.Error("Missing custom store class configuration for CustomStateStoreModeProvider");
                throw new InvalidOperationException("Missing custom store class configuration");
            }

        }

        public Props ShardStoreProps(ShardId shardId)
        {
            return customStore.ShardStoreProps(shardId);
        }

        public Props CoordinatorStoreProps()
        {
            return customStore.CoordinatorStoreProps();
        }
    }
}
