//-----------------------------------------------------------------------
// <copyright file="EventSourcedRememberEntitiesProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Pattern;

namespace Akka.Cluster.Sharding.Internal
{
    internal sealed class EventSourcedRememberEntitiesProvider : IRememberEntitiesProvider
    {
        public EventSourcedRememberEntitiesProvider(string typeName, ClusterShardingSettings settings)
        {
            TypeName = typeName;
            Settings = settings;
        }

        public string TypeName { get; }

        public ClusterShardingSettings Settings { get; }

        /// <summary>
        /// this is backed by an actor using the same events, at the serialization level, as the now removed PersistentShard when state-store-mode=persistence
        /// new events can be added but the old events should continue to be handled
        /// </summary>
        /// <param name="shardId"></param>
        /// <returns></returns>
        public Props ShardStoreProps(string shardId)
        {
            var backoffOptions = Backoff.OnStop(
                EventSourcedRememberEntitiesShardStore.Props(TypeName, shardId, Settings),
                childName: "shardstore",
                minBackoff: Settings.TuningParameters.ShardFailureBackoff,
                maxBackoff: Settings.TuningParameters.ShardFailureBackoff,
                randomFactor: 0.2,
                maxNrOfRetries: -1);
            
            return BackoffSupervisor.Props(backoffOptions);
        }

        /// <summary>
        /// Note that this one is never used for the deprecated persistent state store mode, only when state store is ddata
        /// combined with eventsourced remember entities storage
        /// </summary>
        /// <returns></returns>
        public Props CoordinatorStoreProps()
        {
            var backoffOptions = Backoff.OnStop(
                EventSourcedRememberEntitiesCoordinatorStore.Props(TypeName, Settings),
                childName: "coordinator",
                minBackoff: Settings.TuningParameters.CoordinatorFailureBackoff,
                maxBackoff: Settings.TuningParameters.CoordinatorFailureBackoff,
                randomFactor: 0.2,
                maxNrOfRetries: -1);
            return BackoffSupervisor.Props(backoffOptions);
        }
    }
}
