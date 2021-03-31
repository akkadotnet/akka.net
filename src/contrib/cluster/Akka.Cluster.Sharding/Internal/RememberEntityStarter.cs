using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    /// <summary>
    /// INTERNAL API: Actor responsible for starting entities when rememberEntities is enabled
    /// </summary>
    internal class RememberEntityStarter : ReceiveActor, IWithTimers
    {
        private sealed class StartBatchMessage : INoSerializationVerificationNeeded
        {
            public StartBatchMessage(int batchSize)
            {
                BatchSize = batchSize;
            }

            public int BatchSize { get; }
        }

        private class ResendUnAcked : INoSerializationVerificationNeeded
        {
            public static readonly ResendUnAcked Instance = new ResendUnAcked();
            private ResendUnAcked() { }
        }

        public static Props Props(
            IActorRef region,
            IActorRef shard,
            ShardId shardId,
            IImmutableSet<EntityId> ids,
            ClusterShardingSettings settings)
        {
            return Actor.Props.Create(() => new RememberEntityStarter(region, shard, shardId, ids, settings));
        }

        private readonly IActorRef _region;
        private readonly IActorRef _shard;
        private readonly ShardId _shardId;
        private readonly ILoggingAdapter _log;

        private IImmutableSet<EntityId> _idsLeftToStart = ImmutableHashSet<string>.Empty;
        private IImmutableSet<EntityId> _waitingForAck = ImmutableHashSet<string>.Empty;
        private IImmutableSet<EntityId> _entitiesMoved = ImmutableHashSet<string>.Empty;

        public RememberEntityStarter(
            IActorRef region,
            IActorRef shard,
            ShardId shardId,
            IImmutableSet<EntityId> ids,
            ClusterShardingSettings settings
            )
        {
            if (ids == null || ids.Count == 0)
                throw new ArgumentException($"{nameof(ids)} must not be null or empty");

            _log = Context.GetLogger();
            _log.Debug("Shard starting [{0}] remembered entities using strategy [{1}]",
                ids.Count,
                settings.TuningParameters.EntityRecoveryStrategy);

            _region = region;
            _shard = shard;
            _shardId = shardId;

            switch (settings.TuningParameters.EntityRecoveryStrategy)
            {
                case "all":
                    StartBatch(ids);
                    break;
                case "constant":
                    _idsLeftToStart = ids;
                    Timers.StartPeriodicTimer(
                        "constant", 
                        new StartBatchMessage(settings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities),
                        settings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
                    StartBatch(settings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
                    break;
            }
            Timers.StartPeriodicTimer("retry", ResendUnAcked.Instance, settings.TuningParameters.RetryInterval);

            Receive<StartBatchMessage>(sb => StartBatch(sb.BatchSize));
            Receive<ShardRegion.StartEntityAck>(se => OnAck(se.EntityId, se.ShardId));
            Receive<ResendUnAcked>(_ => RetryUnacked());
        }

        private void OnAck(EntityId entityId, ShardId ackFromShardId)
        {
            _idsLeftToStart = _idsLeftToStart.Remove(entityId);
            _waitingForAck = _waitingForAck.Remove(entityId);
            if (_shardId != ackFromShardId)
                _entitiesMoved = _entitiesMoved.Add(entityId);
            if (_waitingForAck.Count == 0 && _idsLeftToStart.Count == 0)
            {
                if (_entitiesMoved.Count > 0)
                {
                    _log.Info("Found [{}] entities moved to new shard(s)", _entitiesMoved.Count);
                    _shard.Tell(new Shard.EntitiesMovedToOtherShard(_entitiesMoved));
                }
                Context.Stop(Self);
            }
        }

        private void StartBatch(int batchSize)
        {
            _log.Debug("Starting batch of [{0}] remembered entities", batchSize);
            var batch = _idsLeftToStart.Take(batchSize);
            _idsLeftToStart = _idsLeftToStart.Drop(batchSize).ToImmutableHashSet();
            StartBatch(batch.ToImmutableHashSet());
        }

        private void StartBatch(IImmutableSet<EntityId> entityIds)
        {
            // these go through the region rather the directly to the shard
            // so that shard id extractor changes make them start on the right shard
            _waitingForAck = _waitingForAck.Union(entityIds);
            foreach (var entityId in entityIds)
            {
                _region.Tell(new ShardRegion.StartEntity(entityId));
            }
        }

        private void RetryUnacked()
        {
            if (_waitingForAck.Count > 0)
            {
                _log.Debug("Found [{0}] remembered entities waiting for StartEntityAck, retrying", _waitingForAck.Count);
                foreach (var id in _waitingForAck)
                {
                    // for now we just retry all (as that was the existing behavior spread out over starter and shard)
                    // but in the future it could perhaps make sense to batch also the retries to avoid thundering herd
                    _region.Tell(new ShardRegion.StartEntity(id));
                }
            }
        }

        public ITimerScheduler Timers { get; set; }
    }
}
