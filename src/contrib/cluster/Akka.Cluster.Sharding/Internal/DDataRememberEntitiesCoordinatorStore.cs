using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;
using Akka.Util;
using Get = Akka.DistributedData.Get;

namespace Akka.Cluster.Sharding.Internal
{
    using ShardId = String;

    internal class DDataRememberEntitiesCoordinatorStore : ReceiveActor
    {
        public static Props Props(string typeName, ClusterShardingSettings settings, IActorRef replicator, int majorityMinCap)
            => Actor.Props.Create(() => new DDataRememberEntitiesCoordinatorStore(typeName, settings, replicator, majorityMinCap));

        private readonly string _typeName;
        private readonly ClusterShardingSettings _settings;
        private readonly IActorRef _replicator;
        private readonly int _majorityMinCap;
        private readonly ILoggingAdapter _log;

        private readonly ReadMajority _readMajority;
        private readonly WriteMajority _writeMajority;

        private readonly GSetKey<string> _allShardsKey;
        private Option<IImmutableSet<ShardId>> _allShards = Option<IImmutableSet<string>>.None;
        private Option<IActorRef> _coordinatorWaitingForShards = Option<IActorRef>.None;

        public DDataRememberEntitiesCoordinatorStore(string typeName, ClusterShardingSettings settings, IActorRef replicator, int majorityMinCap)
        {
            _typeName = typeName;
            _settings = settings;
            _replicator = replicator;
            _majorityMinCap = majorityMinCap;

            _log = Context.GetLogger();

            _readMajority = new ReadMajority(settings.TuningParameters.WaitingForStateTimeout, majorityMinCap);
            _writeMajority = new WriteMajority(settings.TuningParameters.UpdatingStateTimeout, majorityMinCap);

            _allShardsKey = new GSetKey<string>($"shard-{typeName}-all");
            GetAllShards();

            Receive<RememberEntitiesCoordinatorStore.GetShards>(_ =>
            {
                _coordinatorWaitingForShards = new Option<IActorRef>(Sender);
                if (_allShards.HasValue)
                {
                    OnGotAllShards(_allShards.Value.ToImmutableHashSet());
                }
            });

            Receive<GetSuccess>(msg =>
            {
                if (!Equals(msg.Key, _allShardsKey))
                    return;
                OnGotAllShards(msg.Get(_allShardsKey).Elements.ToImmutableHashSet());
            });

            Receive<NotFound>(msg =>
            {
                if (!Equals(msg.Key, _allShardsKey))
                    return;
                OnGotAllShards(ImmutableHashSet<string>.Empty);
            });

            Receive<GetFailure>(msg =>
            {
                if (!Equals(msg.Key, _allShardsKey))
                    return;
                _log.Error(
                    "The ShardCoordinator was unable to get all shards state within 'waiting-for-state-timeout': {0} millis (retrying)",
                    _readMajority.Timeout.TotalMilliseconds);
                // repeat until GetSuccess
                GetAllShards();
            });

            Receive<RememberEntitiesCoordinatorStore.AddShard>(msg =>
            {
                var shardId = msg.EntityId;
                _replicator.Tell(
                    new Update(
                        _allShardsKey, 
                        GSet<string>.Empty, 
                        _writeMajority, 
                        data => ((GSet<string>) data).Add(msg.EntityId), 
                        new Option<(IActorRef, string)>((Sender, shardId))));
            });

            Receive<UpdateSuccess>(msg =>
            {
                if (!Equals(msg.Key, _allShardsKey))
                    return;

                var (replyTo, shardId) = ((Option<(IActorRef, string)>)msg.Request).Value;
                _log.Debug("The coordinator shards state was successfully updated with {0}", shardId);
                replyTo.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(shardId));
            });

            Receive<UpdateTimeout>(msg =>
            {
                if (!Equals(msg.Key, _allShardsKey))
                    return;

                var (replyTo, shardId) = ((Option<(IActorRef, string)>)msg.Request).Value;
                _log.Error("The ShardCoordinator was unable to update shards distributed state within 'updating-state-timeout': {} millis (retrying), adding shard={}",
                    _writeMajority.Timeout.TotalMilliseconds,
                    shardId);
                replyTo.Tell(new RememberEntitiesCoordinatorStore.UpdateFailed(shardId));
            });

            Receive<ModifyFailure>(msg =>
            {
                var (replyTo, shardId) = ((Option<(IActorRef, string)>)msg.Request).Value;
                _log.Error(msg.Cause,
                    "The remember entities store was unable to add shard [{0}] (key [{1}], failed with error: {2})",
                    shardId,
                    msg.Key,
                    msg.ErrorMessage);
                replyTo.Tell(new RememberEntitiesCoordinatorStore.UpdateFailed(shardId));
            });
        }

        private Cluster Node => Cluster.Get(Context.System);
        private UniqueAddress SelfUniqueAddress => Node.SelfUniqueAddress;

        // eager load of remembered shard ids
        private void GetAllShards()
            => _replicator.Tell(new Get(_allShardsKey, _readMajority));

        private void OnGotAllShards(IImmutableSet<ShardId> shardIds)
        {
            if (_coordinatorWaitingForShards.HasValue)
            {
                _coordinatorWaitingForShards.Value.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(shardIds));
                _coordinatorWaitingForShards = Option<IActorRef>.None;
                // clear the shards out now that we have sent them to coordinator, to save some memory
                _allShards = Option<IImmutableSet<string>>.None;
                return;
            }

            _allShards = new Option<IImmutableSet<string>>(shardIds);
        }
    }
}
