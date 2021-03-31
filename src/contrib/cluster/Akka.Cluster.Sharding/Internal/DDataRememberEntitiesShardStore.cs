using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Dispatch;
using Akka.DistributedData;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Get = Akka.DistributedData.Get;
// ReSharper disable BuiltInTypeReferenceStyle

namespace Akka.Cluster.Sharding.Internal
{
    using ShardId = String;
    using EntityId = String;

    internal class DDataRememberEntitiesShardStore : ReceiveActor, IWithUnboundedStash
    {
        #region statics

        public static Props Props(ShardId shardId, string typeName, ClusterShardingSettings settings, IActorRef replicator, int majorityMinCap)
            => Actor.Props.Create(() => new DDataRememberEntitiesShardStore(shardId, typeName, settings, replicator, majorityMinCap));

        // The default maximum-frame-size is 256 KiB with Artery.
        // When using entity identifiers with 36 character strings (e.g. UUID.randomUUID).
        // By splitting the elements over 5 keys we can support 10000 entities per shard.
        // The Gossip message size of 5 ORSet with 2000 ids is around 200 KiB.
        private const int NumberOfKeys = 5;

        private static ORSetKey<EntityId>[] StateKeys(string typeName, ShardId shardId)
            => Enumerable.Range(0, NumberOfKeys).Select(i => new ORSetKey<EntityId>($"shard-{shardId}-{i}")).ToArray();

        private interface IEvt
        {
            EntityId Id { get; }
        }
        
        private sealed class EvtComparer : IEqualityComparer<IEvt>
        {
            public bool Equals(IEvt x, IEvt y)
            {
                if (x == null || y == null) return false;
                return x.Id == y.Id;
            }

            public int GetHashCode(IEvt obj)
            {
                return (obj.Id != null ? obj.Id.GetHashCode() : 0);
            }
        }

        private static readonly EvtComparer EvtComparerObject = new EvtComparer();

        private sealed class Started : IEvt
        {
            public Started(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public override string ToString()
                => Id;

            public override int GetHashCode()
                => Id.GetHashCode();
        }

        private sealed class Stopped : IEvt
        {
            public Stopped(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public override string ToString()
                => Id;

            public override int GetHashCode()
                => Id.GetHashCode();
        }

        #region Receive state tracking

        internal abstract class ReceiveWithState
        {
            public abstract bool Receive(object message);
        }

        private sealed class WaitingForAllEntityIds : ReceiveWithState
        {
            public WaitingForAllEntityIds(DDataRememberEntitiesShardStore store, ImmutableHashSet<int> gotKeys, IImmutableSet<string> ids, Option<IActorRef> shardWaiting)
            {
                _gotKeys = gotKeys;
                _ids = ids;
                _shardWaiting = shardWaiting;
                _store = store;
            }

            private readonly DDataRememberEntitiesShardStore _store;
            private readonly ImmutableHashSet<int> _gotKeys;
            private readonly IImmutableSet<EntityId> _ids;
            private readonly Option<IActorRef> _shardWaiting;

            void ReceiveOne(int i, IImmutableSet<EntityId> idsForKey)
            {
                var newGotKeys = _gotKeys.Add(i);
                var newIds = _ids.Union(idsForKey).ToImmutableHashSet();

                if (newGotKeys.Count == NumberOfKeys)
                {
                    if (_shardWaiting.HasValue)
                    {
                        _store._log.Debug("Shard waiting for remembered entities, sending remembered and going idle");
                        var shard = _shardWaiting.Value;
                        shard.Tell(new RememberEntitiesShardStore.RememberedEntities(newIds));
                        _store.Become(_store.Idle);
                        _store.Stash.UnstashAll();
                        return;
                    }

                    // we haven't seen request yet
                    _store._log.Debug("Got remembered entities, waiting for shard to request them");
                    _store.Become(new WaitingForAllEntityIds(_store, newGotKeys, newIds, Option<IActorRef>.None));
                    return;
                }

                _store.Become(new WaitingForAllEntityIds(_store, newGotKeys, newIds, _shardWaiting));
            }

            public override bool Receive(object message)
            {
                switch (message)
                {
                    case GetSuccess g:
                    {
                        var i = ((Option<int>)g.Request).Value;
                        var key = _store._keys[i];
                        var ids = g.Get(key).Elements;
                        ReceiveOne(i, ids);
                        return true;
                    }

                    case NotFound nf:
                    {
                        var i = ((Option<int>)nf.Request).Value;
                        ReceiveOne(i, ImmutableHashSet<string>.Empty);
                        return true;
                    }

                    case GetFailure gf:
                    {
                        _store._log.Error(
                            "Unable to get an initial state within 'waiting-for-state-timeout': [{0}] using [{1}] (key [{2}])",
                            _store._readMajority.Timeout,
                            _store._readMajority,
                            gf.Key);
                        Context.Stop(_store.Self);
                        return true;
                    }

                    case GetDataDeleted _:
                    {
                        _store._log.Error("Unable to get an initial state because it was deleted");
                        Context.Stop(_store.Self);
                        return true;
                    }

                    case RememberEntitiesShardStore.Update update:
                        _store._log.Warning("Got an update before load of initial entities completed, dropping update: [{0}]",
                            update);
                        return true;

                    case RememberEntitiesShardStore.GetEntities _:
                        if (_gotKeys.Count == NumberOfKeys)
                        {
                            // we already got all and was waiting for a request
                            _store._log.Debug("Got request from shard, sending remembered entities");
                            _store.Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(_ids));
                            _store.Become(_store.Idle);
                            _store.Stash.UnstashAll();
                            return true;
                        }

                        // we haven't seen all ids yet
                        _store._log.Debug("Got request from shard, waiting for all remembered entities to arrive");
                        _store.Become(new WaitingForAllEntityIds(_store, _gotKeys, _ids, new Option<IActorRef>(_store.Sender)));
                        return true;

                    default:
                        // if we get a write while waiting for the listing, defer it until we saw listing, if not we can get a mismatch
                        // of remembered with what the shard thinks it just wrote
                        _store.Stash.Stash();
                        return true;
                }

            }
        }

        private sealed class WaitingForUpdates : ReceiveWithState
        {
            public WaitingForUpdates(
                DDataRememberEntitiesShardStore store, 
                IActorRef requestor, 
                RememberEntitiesShardStore.Update update, 
                ImmutableDictionary<HashSet<IEvt>, (Update, int)> allUpdates)
            {
                _requestor = requestor;
                _update = update;
                _updatesLeft = allUpdates;
                _store = store;
            }

            private readonly DDataRememberEntitiesShardStore _store;
            private readonly IActorRef _requestor;
            private readonly RememberEntitiesShardStore.Update _update;

            // updatesLeft used both to keep track of what work remains and for retrying on timeout up to a limit
            //public List<KeyValuePair<HashSet<IEvt>, (Update update, int maxUpdateAttempts)>> UpdatesLeft { get; }
            private readonly ImmutableDictionary<HashSet<IEvt>, (Update, int)> _updatesLeft;

            public override bool Receive(object message)
            {
                switch (message)
                {
                    case UpdateSuccess msg:
                    {
                        var events = (HashSet<IEvt>)msg.Request;
                    
                        _store._log.Debug("The DDataShard state was successfully updated for [{0}]", string.Join(",", events));

                        // Greg: Yes, there is a possible NRE here, even the JVM code has to use @unchecked
                        // to force this code.
                        var remainingAfterThis = _updatesLeft.Remove(GetReferenceKey(_updatesLeft, events));
                        if (remainingAfterThis.Count == 0)
                        {
                            _requestor.Tell(
                                new RememberEntitiesShardStore.UpdateDone(
                                    _update.Started, 
                                    _update.Stopped));
                            _store.Become(_store.Idle);
                            return true;
                        }

                        _store.Become(new WaitingForUpdates(_store, _requestor, _update, remainingAfterThis));
                        return true;
                    }

                    case UpdateTimeout msg:
                    {
                        var events = (HashSet<IEvt>)msg.Request;
                        var refKey = GetReferenceKey(_updatesLeft, events);
                        var (updateForEvents, retriesLeft) = _updatesLeft[refKey];
                        if (retriesLeft > 0)
                        {
                            _store._log.Debug("Retrying update because of write timeout, tries left [{0}]", retriesLeft);
                            _store._replicator.Tell(updateForEvents);
                            _store.Become(new WaitingForUpdates(
                                _store, _requestor, _update, _updatesLeft.SetItem(refKey, (updateForEvents, retriesLeft - 1))));
                            return true;
                        }

                        _store._log.Error("Unable to update state, within 'updating-state-timeout'= [{0}], gave up after [{1}] retries",
                            _store._writeMajority.Timeout,
                            MaxUpdateAttempts);
                        return true;
                    }

                    case StoreFailure _:
                    {
                        _store._log.Error("Unable to update state, due to store failure");
                        // will trigger shard restart
                        Context.Stop(_store.Self);
                        return true;
                    }

                    case ModifyFailure msg:
                    {
                        _store._log.Error(msg.Cause, "Unable to update state, due to modify failure: {0}", msg.ErrorMessage);
                        // will trigger shard restart
                        Context.Stop(_store.Self);
                        return true;
                    }

                    case UpdateDataDeleted _:
                    {
                        _store._log.Error("Unable to update state, due to delete");
                        // will trigger shard restart
                        Context.Stop(_store.Self);
                        return true;
                    }

                    case RememberEntitiesShardStore.Update update:
                        _store._log.Warning("Got a new update before write of previous completed, dropping update: [{0}]", update);
                        return true;

                    default:
                        return false;
                }
            }

            // scala can use a Set as keys for dictionaries, C# doesn't (reference equality, not value)
            private HashSet<IEvt> GetReferenceKey(
                ImmutableDictionary<HashSet<IEvt>, (Update update, int maxUpdateAttempts)> dict,
                HashSet<IEvt> valueKey)
            {
                var comparableKey = new HashSet<IEvt>(valueKey, EvtComparerObject);
                return dict.Keys.First(key => comparableKey.SetEquals(key));
            }
        }

        #endregion

        #endregion

        private readonly IActorRef _replicator;
        private readonly ILoggingAdapter _log;

        private readonly ReadMajority _readMajority;
        private readonly WriteMajority _writeMajority;
        private const int MaxUpdateAttempts = 3;
        private readonly ORSetKey<EntityId>[] _keys;

        private readonly UniqueAddress _selfUniqueAddress;

        public DDataRememberEntitiesShardStore(string shardId, string typeName, ClusterShardingSettings settings, IActorRef replicator, int majorityMinCap)
        {
            _replicator = replicator;

            _readMajority = new ReadMajority(settings.TuningParameters.WaitingForStateTimeout, majorityMinCap);
            _writeMajority = new WriteMajority(settings.TuningParameters.UpdatingStateTimeout, majorityMinCap);
            _keys = StateKeys(typeName, shardId);

            _log = Context.GetLogger();

            if(_log.IsDebugEnabled)
                _log.Debug("Starting up DDataRememberEntitiesStore, read timeout: [{}], write timeout: [{}], majority min cap: [{}]",
                    settings.TuningParameters.WaitingForStateTimeout,
                    settings.TuningParameters.UpdatingStateTimeout,
                    majorityMinCap);

            _selfUniqueAddress = Cluster.Get(Context.System).SelfUniqueAddress;

            LoadAllEntities();
            Become(new WaitingForAllEntityIds(this, ImmutableHashSet<int>.Empty, ImmutableHashSet<EntityId>.Empty,
                Option<IActorRef>.None));
        }

        public IStash Stash { get; set; }

        private ORSetKey<EntityId> Key(EntityId entityId)
        {
            var i = Math.Abs(entityId.GetHashCode() % NumberOfKeys);
            return _keys[i];
        }

        private void LoadAllEntities()
        {
            for (var i = 0; i < NumberOfKeys; i++)
            {
                _replicator.Tell(new Get(_keys[i], _readMajority, new Option<int>(i)));
            }
        }

        private Receive Idle => message =>
        {
            switch (message)
            {
                case RememberEntitiesShardStore.GetEntities _:
                    // not supported, but we may get several if the shard timed out and retried
                    _log.Debug("Another get entities request after responding to one, not expected/supported, ignoring");
                    return true;
                case RememberEntitiesShardStore.Update update:
                    OnUpdate(update);
                    return true;
                default:
                    return false;
            }
        };

        private void OnUpdate(RememberEntitiesShardStore.Update update)
        {
            var grouping = update.Started.Select(s => new Started(s))
                .Union<IEvt>(update.Stopped.Select(s => new Stopped(s)))
                .GroupBy(e => Key(e.Id));

            // map from set of evts (for same ddata key) to one update that applies each of them
            ImmutableDictionary<HashSet<IEvt>, (Update update, int)> dDataUpdates = 
                grouping.ToImmutableDictionary(
                    keySelector: g => new HashSet<IEvt>(g.AsEnumerable(), EvtComparerObject),
                    elementSelector: g =>
                    {
                        var u = new Update(
                            key:g.Key, 
                            initial:ORSet<EntityId>.Empty, 
                            consistency: _writeMajority, 
                            modify: d =>
                            {
                                var existing = (ORSet<EntityId>)d;
                                foreach (var evt in g)
                                {
                                    existing = evt is Started 
                                        ? existing.Add(_selfUniqueAddress, evt.Id) 
                                        : existing.Remove(_selfUniqueAddress, evt.Id);
                                }

                                return existing;
                            },
                            request: new Option<HashSet<IEvt>>(new HashSet<IEvt>(g.AsEnumerable())));
                        return (u, MaxUpdateAttempts);
                    });

            foreach (var kvp in dDataUpdates)
            {
                _replicator.Tell(kvp.Value.update);
            }

            Become(new WaitingForUpdates(this, Sender, update, dDataUpdates));
        }

        private void Become(ReceiveWithState state)
        {
            Context.Become(state.Receive);
        }
    }
}
