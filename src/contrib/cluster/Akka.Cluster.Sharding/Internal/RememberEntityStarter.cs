//-----------------------------------------------------------------------
// <copyright file="RememberEntityStarter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    internal class RememberEntityStarter : ActorBase, IWithTimers
    {
        public static Props Props(
              IActorRef region,
              IActorRef shard,
              ShardId shardId,
              IImmutableSet<EntityId> ids,
              ClusterShardingSettings settings)
        {
            return Actor.Props.Create(() => new RememberEntityStarter(region, shard, shardId, ids, settings));
        }

        //  private final case class StartBatch(batchSize: Int) extends NoSerializationVerificationNeeded
        private class StartBatch
        {
            public StartBatch(int batchSize)
            {
                BatchSize = batchSize;
            }

            public int BatchSize { get; }
        }

        //  private case object ResendUnAcked extends NoSerializationVerificationNeeded
        private sealed class ResendUnAcked
        {
            public static readonly ResendUnAcked Instance = new ResendUnAcked();

            private ResendUnAcked()
            {
            }
        }

        private readonly IActorRef region;
        private readonly IActorRef shard;
        private readonly string shardId;

        private IImmutableSet<EntityId> idsLeftToStart = ImmutableHashSet<EntityId>.Empty;
        private IImmutableSet<EntityId> waitingForAck = ImmutableHashSet<EntityId>.Empty;
        private IImmutableSet<EntityId> entitiesMoved = ImmutableHashSet<EntityId>.Empty;

        public RememberEntityStarter(
            IActorRef region,
            IActorRef shard,
            ShardId shardId,
            IImmutableSet<EntityId> ids,
            ClusterShardingSettings settings)
        {
            if (ids == null || ids.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(ids));
            this.region = region;
            this.shard = shard;
            this.shardId = shardId;

            Log.Debug(
              "Shard starting [{0}] remembered entities using strategy [{1}]",
              ids.Count,
              settings.TuningParameters.EntityRecoveryStrategy);

            switch (settings.TuningParameters.EntityRecoveryStrategy)
            {
                case "all":
                    idsLeftToStart = ImmutableHashSet<EntityId>.Empty;
                    OnStartBatch(ids);
                    break;
                case "constant":
                    idsLeftToStart = ids;
                    Timers.StartPeriodicTimer(
                        "constant",
                        new StartBatch(settings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities),
                        settings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
                    OnStartBatch(settings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
                    break;
            }
            Timers.StartPeriodicTimer("retry", ResendUnAcked.Instance, settings.TuningParameters.RetryInterval);
        }

        public ITimerScheduler Timers { get; set; }

        public ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case StartBatch start:
                    OnStartBatch(start.BatchSize);
                    return true;
                case ShardRegion.StartEntityAck ack:
                    OnAck(ack.EntityId, ack.ShardId);
                    return true;
                case ResendUnAcked _:
                    OnRetryUnacked();
                    return true;
            }
            return false;
        }

        private void OnAck(EntityId entityId, ShardId ackFromShardId)
        {
            idsLeftToStart = idsLeftToStart.Remove(entityId);
            waitingForAck = waitingForAck.Remove(entityId);
            if (shardId != ackFromShardId)
                entitiesMoved = entitiesMoved.Add(entityId);
            if (waitingForAck.Count == 0 && idsLeftToStart.Count == 0)
            {
                if (entitiesMoved.Count != 0)
                {
                    Log.Info("Found [{0}] entities moved to new shard(s)", entitiesMoved.Count);
                    shard.Tell(new Shard.EntitiesMovedToOtherShard(entitiesMoved));
                }
                Context.Stop(Self);
            }
        }

        private void OnStartBatch(int batchSize)
        {
            Log.Debug("Starting batch of [{0}] remembered entities", batchSize);
            var ids = idsLeftToStart.ToList();
            var batch = ids.Take(batchSize).ToImmutableHashSet();
            var newIdsLeftToStart = ids.Skip(batchSize).ToImmutableHashSet();
            idsLeftToStart = newIdsLeftToStart;
            OnStartBatch(batch);
        }

        private void OnStartBatch(IImmutableSet<EntityId> entityIds)
        {
            // these go through the region rather the directly to the shard
            // so that shard id extractor changes make them start on the right shard
            waitingForAck = waitingForAck.Union(entityIds);
            foreach (var entityId in entityIds)
                region.Tell(new ShardRegion.StartEntity(entityId));
        }

        private void OnRetryUnacked()
        {
            if (waitingForAck.Count != 0)
            {
                Log.Debug("Found [{0}] remembered entities waiting for StartEntityAck, retrying", waitingForAck.Count);
                foreach (var id in waitingForAck)
                {
                    // for now we just retry all (as that was the existing behavior spread out over starter and shard)
                    // but in the future it could perhaps make sense to batch also the retries to avoid thundering herd
                    region.Tell(new ShardRegion.StartEntity(id));
                }
            }
        }
    }
}
