//-----------------------------------------------------------------------
// <copyright file="DDataRememberEntitiesCoordinatorStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly string _typeName;
        private readonly IActorRef _replicator;

        private readonly Cluster _node;
        private readonly UniqueAddress _selfUniqueAddress;
        private readonly IReadConsistency _readMajority;
        private readonly IWriteConsistency _writeMajority;
        private readonly GSetKey<string> _allShardsKey;
        private IImmutableSet<ShardId> _allShards = null;
        private IActorRef _coordinatorWaitingForShards = null;

        public DDataRememberEntitiesCoordinatorStore(
            string typeName,
            ClusterShardingSettings settings,
            IActorRef replicator,
            int majorityMinCap)
        {
            _typeName = typeName;
            _replicator = replicator;

            _node = Cluster.Get(Context.System);
            _selfUniqueAddress = _node.SelfUniqueAddress;

            _readMajority = new ReadMajority(settings.TuningParameters.WaitingForStateTimeout, majorityMinCap);
            _writeMajority = new WriteMajority(settings.TuningParameters.UpdatingStateTimeout, majorityMinCap);

            _allShardsKey = new GSetKey<string>($"shard-{typeName}-all");

            // eager load of remembered shard ids
            GetAllShards();
        }

        private void GetAllShards()
        {
            _replicator.Tell(Dsl.Get(_allShardsKey, _readMajority));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.GetShards _:
                    if (_allShards != null)
                    {
                        _coordinatorWaitingForShards = Sender;
                        OnGotAllShards(_allShards);
                    }
                    else
                    {
                        // reply when we get them, since there is only ever one coordinator communicating with us
                        // and it may retry we can just keep the latest sender
                        _coordinatorWaitingForShards = Sender;
                    }
                    return true;

                case GetSuccess g when g.Key.Equals(_allShardsKey):
                    OnGotAllShards(g.Get(_allShardsKey).Elements);
                    return true;

                case NotFound m when m.Key.Equals(_allShardsKey):
                    OnGotAllShards(ImmutableHashSet<ShardId>.Empty);
                    return true;

                case GetFailure m when m.Key.Equals(_allShardsKey):
                    _log.Error("The ShardCoordinator was unable to get all shards state within 'waiting-for-state-timeout': {0} millis (retrying)",
                        _readMajority.Timeout.TotalMilliseconds);
                    // repeat until GetSuccess
                    GetAllShards();
                    return true;

                case RememberEntitiesCoordinatorStore.AddShard m:
                    _replicator.Tell(Dsl.Update(_allShardsKey, GSet<string>.Empty, _writeMajority, (Sender, m.ShardId), reg => reg.Add(m.ShardId)));
                    return true;

                case UpdateSuccess m when m.Key.Equals(_allShardsKey) && m.Request is ValueTuple<IActorRef, ShardId> r:
                    _log.Debug("The coordinator shards state was successfully updated with {0}", r.Item2);
                    r.Item1.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(r.Item2));
                    return true;

                case UpdateTimeout m when m.Key.Equals(_allShardsKey) && m.Request is ValueTuple<IActorRef, ShardId> r:
                    _log.Error("The ShardCoordinator was unable to update shards distributed state within 'updating-state-timeout': {0} millis (retrying), adding shard={1}",
                        _writeMajority.Timeout.TotalMilliseconds,
                        r.Item2);
                    r.Item1.Tell(new RememberEntitiesCoordinatorStore.UpdateFailed(r.Item2));
                    return true;

                case ModifyFailure m when m.Request is ValueTuple<IActorRef, ShardId> r:
                    _log.Error(
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
            if (_coordinatorWaitingForShards != null)
            {
                _coordinatorWaitingForShards.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(shardIds));
                _coordinatorWaitingForShards = null;
                // clear the shards out now that we have sent them to coordinator, to save some memory
                _allShards = null;
            }
            else
            {
                // wait for coordinator to ask
                _allShards = shardIds;
            }
        }
    }
}
