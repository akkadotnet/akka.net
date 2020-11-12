//-----------------------------------------------------------------------
// <copyright file="DDataRememberEntitiesCoordinatorStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;

namespace Akka.Cluster.Sharding.Internal
{
    using ShardId = String;

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class DDataRememberEntitiesCoordinatorStore : ActorBase
    {
        public static Props Props(
            string typeName,
            ClusterShardingSettings settings,
            IActorRef replicator,
            int majorityMinCap)
        {
            return Actor.Props.Create(() => new DDataRememberEntitiesCoordinatorStore(typeName, settings, replicator, majorityMinCap));
        }

        private readonly ILoggingAdapter log = Context.GetLogger();
        private readonly string typeName;
        private readonly IActorRef replicator;

        private readonly Cluster node;
        private readonly UniqueAddress selfUniqueAddress;
        private readonly IReadConsistency readMajority;
        private readonly IWriteConsistency writeMajority;
        private readonly GSetKey<string> allShardsKey;
        private IImmutableSet<ShardId> allShards = null;
        private IActorRef coordinatorWaitingForShards = null;

        public DDataRememberEntitiesCoordinatorStore(
            string typeName,
            ClusterShardingSettings settings,
            IActorRef replicator,
            int majorityMinCap)
        {
            this.typeName = typeName;
            this.replicator = replicator;

            node = Cluster.Get(Context.System);
            selfUniqueAddress = node.SelfUniqueAddress;

            readMajority = new ReadMajority(settings.TuningParameters.WaitingForStateTimeout, majorityMinCap);
            writeMajority = new WriteMajority(settings.TuningParameters.UpdatingStateTimeout, majorityMinCap);

            allShardsKey = new GSetKey<string>($"shard-{typeName}-all");

            // eager load of remembered shard ids
            GetAllShards();
        }

        private void GetAllShards()
        {
            replicator.Tell(Dsl.Get(allShardsKey, readMajority));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.GetShards _:
                    if (allShards != null)
                    {
                        coordinatorWaitingForShards = Sender;
                        OnGotAllShards(allShards);
                    }
                    else
                    {
                        // reply when we get them, since there is only ever one coordinator communicating with us
                        // and it may retry we can just keep the latest sender
                        coordinatorWaitingForShards = Sender;
                    }
                    return true;

                case GetSuccess g when g.Key.Equals(allShardsKey):
                    OnGotAllShards(g.Get(allShardsKey).Elements);
                    return true;

                case NotFound m when m.Key.Equals(allShardsKey):
                    OnGotAllShards(ImmutableHashSet<ShardId>.Empty);
                    return true;

                case GetFailure m when m.Key.Equals(allShardsKey):
                    log.Error("The ShardCoordinator was unable to get all shards state within 'waiting-for-state-timeout': {0} millis (retrying)",
                        readMajority.Timeout.TotalMilliseconds);
                    // repeat until GetSuccess
                    GetAllShards();
                    return true;

                case RememberEntitiesCoordinatorStore.AddShard m:
                    replicator.Tell(Dsl.Update(allShardsKey, GSet<string>.Empty, writeMajority, (Sender, m.ShardId), reg => reg.Add(m.ShardId)));
                    return true;

                case UpdateSuccess m when m.Key.Equals(allShardsKey) && m.Request is ValueTuple<IActorRef, ShardId> r:
                    log.Debug("The coordinator shards state was successfully updated with {0}", r.Item2);
                    r.Item1.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(r.Item2));
                    return true;

                case UpdateTimeout m when m.Key.Equals(allShardsKey) && m.Request is ValueTuple<IActorRef, ShardId> r:
                    log.Error("The ShardCoordinator was unable to update shards distributed state within 'updating-state-timeout': {0} millis (retrying), adding shard={1}",
                        writeMajority.Timeout.TotalMilliseconds,
                        r.Item2);
                    r.Item1.Tell(new RememberEntitiesCoordinatorStore.UpdateFailed(r.Item2));
                    return true;

                case ModifyFailure m when m.Request is ValueTuple<IActorRef, ShardId> r:
                    log.Error(
                        m.Cause,
                        "The remember entities store was unable to add shard [{0}] (key [{1}], failed with error: {2})",
                        r.Item2,
                        m.Key,
                        m.ErrorMessage);
                    r.Item1.Tell(new RememberEntitiesCoordinatorStore.UpdateFailed(r.Item2));
                    return true;
            }
            return false;
        }
        private void OnGotAllShards(IImmutableSet<ShardId> shardIds)
        {
            if (coordinatorWaitingForShards != null)
            {
                coordinatorWaitingForShards.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(shardIds));
                coordinatorWaitingForShards = null;
                // clear the shards out now that we have sent them to coordinator, to save some memory
                allShards = null;
            }
            else
            {
                // wait for coordinator to ask
                allShards = shardIds;
            }
        }
    }
}
