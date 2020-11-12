//-----------------------------------------------------------------------
// <copyright file="DDataRememberEntitiesShardStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class DDataRememberEntitiesShardStore : ActorBase, IWithUnboundedStash
    {
        public static Props Props(
            ShardId shardId,
            string typeName,
            ClusterShardingSettings settings,
            IActorRef replicator,
            int majorityMinCap)
        {
            return Actor.Props.Create(() => new DDataRememberEntitiesShardStore(shardId, typeName, settings, replicator, majorityMinCap));
        }

        /// <summary>
        /// The default maximum-frame-size is 256 KiB with Artery.
        /// When using entity identifiers with 36 character strings (e.g. UUID.randomUUID).
        /// By splitting the elements over 5 keys we can support 10000 entities per shard.
        /// The Gossip message size of 5 ORSet with 2000 ids is around 200 KiB.
        /// This is by intention not configurable because it's important to have the same
        /// configuration on each node.
        /// </summary>
        private const int numberOfKeys = 5;

        private static ImmutableArray<ORSetKey<EntityId>> StateKeys(string typeName, ShardId shardId)
        {
            return Enumerable.Range(0, numberOfKeys).Select(i => new ORSetKey<EntityId>($"shard-{typeName}-{shardId}-{i}")).ToImmutableArray();
        }

        private interface IEvt
        {
            EntityId Id { get; }
        }

        private sealed class Started : IEvt, IEquatable<Started>
        {
            public Started(EntityId id)
            {
                Id = id;
            }

            public string Id { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as Started);
            }

            public bool Equals(Started other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Id.Equals(other.Id);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Id.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"Started({Id})";

            #endregion
        }

        private sealed class Stopped : IEvt, IEquatable<Stopped>
        {
            public Stopped(EntityId id)
            {
                Id = id;
            }

            public string Id { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as Stopped);
            }

            public bool Equals(Stopped other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Id.Equals(other.Id);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Id.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"Stopped({Id})";

            #endregion
        }

        private readonly ILoggingAdapter log = Context.GetLogger();
        private readonly IActorRef replicator;

        private readonly Cluster node;
        private readonly UniqueAddress selfUniqueAddress;
        private readonly IReadConsistency readMajority;
        private readonly IWriteConsistency writeMajority;
        private readonly int maxUpdateAttempts = 3;
        private readonly ImmutableArray<ORSetKey<EntityId>> keys;

        public IStash Stash { get; set; }

        public DDataRememberEntitiesShardStore(
            ShardId shardId,
            string typeName,
            ClusterShardingSettings settings,
            IActorRef replicator,
            int majorityMinCap)
        {
            this.replicator = replicator;

            //  implicit val ec: ExecutionContext = context.dispatcher
            node = Cluster.Get(Context.System);
            selfUniqueAddress = node.SelfUniqueAddress;

            readMajority = new ReadMajority(settings.TuningParameters.WaitingForStateTimeout, majorityMinCap);
            // Note that the timeout is actually updatingStateTimeout / 4 so that we fit 3 retries and a response in the timeout before the shard sees it as a failure
            writeMajority = new WriteMajority(new TimeSpan(settings.TuningParameters.UpdatingStateTimeout.Ticks / 4), majorityMinCap);
            keys = StateKeys(typeName, shardId);

            if (log.IsDebugEnabled)
            {
                log.Debug("Starting up DDataRememberEntitiesStore, read timeout: [{0}], write timeout: [{1}], majority min cap: [{2}]",
                  settings.TuningParameters.WaitingForStateTimeout,
                  settings.TuningParameters.UpdatingStateTimeout,
                  majorityMinCap);
            }
            LoadAllEntities();
            Context.Become(WaitingForAllEntityIds(ImmutableHashSet<int>.Empty, ImmutableHashSet<EntityId>.Empty, null));
        }


        private ORSetKey<EntityId> Key(EntityId entityId)
        {
            var i = Math.Abs(MurmurHash.StringHash(entityId) % numberOfKeys);
            return keys[i];
        }

        protected override bool Receive(object message)
        {
            throw new IllegalStateException("Default receive never expected to actually be used");
        }

        private bool Idle(object message)
        {
            switch (message)
            {
                case RememberEntitiesShardStore.GetEntities _:
                    // not supported, but we may get several if the shard timed out and retried
                    log.Debug("Another get entities request after responding to one, not expected/supported, ignoring");
                    return true;
                case RememberEntitiesShardStore.Update update:
                    OnUpdate(update);
                    return true;
            }
            return false;
        }

        private Receive WaitingForAllEntityIds(IImmutableSet<int> gotKeys, IImmutableSet<EntityId> ids, IActorRef shardWaiting)
        {
            void ReceiveOne(int i, IImmutableSet<EntityId> idsForKey)
            {
                var newGotKeys = gotKeys.Add(i);
                var newIds = ids.Union(idsForKey);
                if (newGotKeys.Count == numberOfKeys)
                {
                    if (shardWaiting != null)
                    {
                        log.Debug("Shard waiting for remembered entities, sending remembered and going idle");
                        shardWaiting.Tell(new RememberEntitiesShardStore.RememberedEntities(newIds));
                        Context.Become(Idle);
                        Stash.UnstashAll();
                    }
                    else
                    {
                        // we haven't seen request yet
                        log.Debug("Got remembered entities, waiting for shard to request them");
                        Context.Become(WaitingForAllEntityIds(newGotKeys, newIds, null));
                    }
                }
                else
                {
                    Context.Become(WaitingForAllEntityIds(newGotKeys, newIds, shardWaiting));
                }
            }

            bool Receive(object message)
            {
                switch (message)
                {
                    case GetSuccess g when g.Request is int i:
                        var key = keys[i];
                        var ids2 = g.Get(key).Elements;
                        ReceiveOne(i, ids2);
                        return true;
                    case NotFound m when m.Request is int i:
                        ReceiveOne(i, ImmutableHashSet<EntityId>.Empty);
                        return true;
                    case GetFailure m:
                        log.Error("Unable to get an initial state within 'waiting-for-state-timeout': [{0}] using [{1}] (key [{2}])",
                            readMajority.Timeout,
                            readMajority,
                            m.Key);
                        Context.Stop(Self);
                        return true;
                    case DataDeleted m:
                        log.Error("Unable to get an initial state because it was deleted");
                        Context.Stop(Self);
                        return true;
                    case RememberEntitiesShardStore.Update update:
                        log.Warning("Got an update before load of initial entities completed, dropping update: [{0}]", update);
                        return true;
                    case RememberEntitiesShardStore.GetEntities _:
                        if (gotKeys.Count == numberOfKeys)
                        {
                            // we already got all and was waiting for a request
                            log.Debug("Got request from shard, sending remembered entities");
                            Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(ids));
                            Context.Become(Idle);
                            Stash.UnstashAll();
                        }
                        else
                        {
                            // we haven't seen all ids yet
                            log.Debug("Got request from shard, waiting for all remembered entities to arrive");
                            Context.Become(WaitingForAllEntityIds(gotKeys, ids, Sender));
                        }
                        return true;
                    case var _:
                        // if we get a write while waiting for the listing, defer it until we saw listing, if not we can get a mismatch
                        // of remembered with what the shard thinks it just wrote
                        Stash.Stash();
                        return true;
                }
            }

            return Receive;
        }

        private void OnUpdate(RememberEntitiesShardStore.Update update)
        {
            var allEvts = update.Started.Select(i => (IEvt)new Started(i)).Union(update.Stopped.Select(i => new Stopped(i))).ToImmutableHashSet();
            // map from set of evts (for same ddata key) to one update that applies each of them
            var ddataUpdates = allEvts.GroupBy(evt => Key(evt.Id)).Select(i =>
            {
                var evts = i.ToImmutableHashSet();
                return new KeyValuePair<IImmutableSet<IEvt>, (Update Update, int MaxUpdateAttempts)>(evts, (Update: Dsl.Update(i.Key, ORSet<EntityId>.Empty, writeMajority, evts, existing =>
                {
                    foreach (var evt in evts)
                    {
                        switch (evt)
                        {
                            case Started s:
                                existing = existing.Add(selfUniqueAddress, s.Id);
                                break;
                            case Stopped s:
                                existing = existing.Remove(selfUniqueAddress, s.Id);
                                break;
                        }
                    }
                    return existing;
                }), MaxUpdateAttempts: maxUpdateAttempts));
            }).ToImmutableDictionary();

            foreach (var u in ddataUpdates)
            {
                replicator.Tell(u.Value.Update);
            }

            Context.Become(WaitingForUpdates(Sender, update, ddataUpdates));
        }

        private Receive WaitingForUpdates(
            IActorRef requestor,
            RememberEntitiesShardStore.Update update,
            ImmutableDictionary<IImmutableSet<IEvt>, (Update Update, int MaxUpdateAttempts)> allUpdates)
        {
            // updatesLeft used both to keep track of what work remains and for retrying on timeout up to a limit
            Receive Next(ImmutableDictionary<IImmutableSet<IEvt>, (Update Update, int MaxUpdateAttempts)> updatesLeft)
            {
                bool Receive(object message)
                {
                    switch (message)
                    {
                        case UpdateSuccess m when m.Request is IImmutableSet<IEvt> evts:
                            log.Debug("The DDataShard state was successfully updated for [{0}]", string.Join(", ", evts));
                            var remainingAfterThis = updatesLeft.Remove(evts);
                            if (remainingAfterThis.IsEmpty)
                            {
                                requestor.Tell(new RememberEntitiesShardStore.UpdateDone(update.Started, update.Stopped));
                                Context.Become(Idle);
                            }
                            else
                            {
                                Context.Become(Next(remainingAfterThis));
                            }
                            return true;
                        case UpdateTimeout m when m.Request is IImmutableSet<IEvt> evts:
                            var (updateForEvts, retriesLeft) = updatesLeft.GetValueOrDefault(evts);
                            if (retriesLeft > 0)
                            {
                                log.Debug("Retrying update because of write timeout, tries left [{0}]", retriesLeft);
                                replicator.Tell(updateForEvts);
                                Context.Become(Next(updatesLeft.SetItem(evts, (updateForEvts, retriesLeft - 1))));
                            }
                            else
                            {
                                log.Error("Unable to update state, within 'updating-state-timeout'= [{0}], gave up after [{1}] retries",
                                    writeMajority.Timeout,
                                    maxUpdateAttempts);
                                // will trigger shard restart
                                Context.Stop(Self);
                            }
                            return true;
                        case StoreFailure m:
                            log.Error("Unable to update state, due to store failure");
                            // will trigger shard restart
                            Context.Stop(Self);
                            return true;
                        case ModifyFailure m:
                            log.Error(m.Cause, "Unable to update state, due to modify failure: {0}", m.ErrorMessage);
                            // will trigger shard restart
                            Context.Stop(Self);
                            return true;
                        case DataDeleted m:
                            log.Error("Unable to update state, due to delete");
                            // will trigger shard restart
                            Context.Stop(Self);
                            return true;
                        case RememberEntitiesShardStore.Update m:
                            log.Warning("Got a new update before write of previous completed, dropping update: [{0}]", m);
                            return true;
                    }

                    return false;
                }

                return Receive;
            }

            return Next(allUpdates);
        }

        private void LoadAllEntities()
        {
            foreach (var i in Enumerable.Range(0, numberOfKeys))
            {
                var key = keys[i];
                replicator.Tell(Dsl.Get(key, readMajority, i));
            }
        }
    }
}
