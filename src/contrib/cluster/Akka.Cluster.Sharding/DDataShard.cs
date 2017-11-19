//-----------------------------------------------------------------------
// <copyright file="DDataShard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DistributedData;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;
    using Msg = Object;

    internal sealed class DDataShardActor : ActorBase, IDDataActorContext
    {
        private readonly DDataShard _shardSemantic;

        public DDataShardActor(
            string typeName,
            ShardId shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage,
            IActorRef replicator,
            int majorityCap)
        {
            Cluster = Cluster.Get(Context.System);
            Replicator = replicator;
            MajorityCap = majorityCap;

            _shardSemantic = new DDataShard(
                context: Context,
                ddataContext: this,
                unhandled: Unhandled,
                typeName: typeName,
                shardId: shardId,
                entityProps: entityProps,
                settings: settings,
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                handOffStopMessage: handOffStopMessage);
        }

        public Cluster Cluster { get; }
        public IActorRef Replicator { get; }
        public int MajorityCap { get; }
        public void Stop()
        {
            Context.Stop(Self);
        }

        IStash IActorStash.Stash { get; set; }

        protected override bool Receive(object message) => _shardSemantic.Receive(message);
    }

    internal interface IDDataActorContext : IWithUnboundedStash
    {
        Cluster Cluster { get; }
        IActorRef Replicator { get; }
        int MajorityCap { get; }

        void Stop();
    }

    /// <summary>
    /// This actor creates children entity actors on demand that it is told to be
    /// responsible for. It is used when `rememberEntities` is enabled and
    /// `state-store-mode=ddata`.
    /// </summary>
    internal sealed class DDataShard : Shard
    {
        private readonly IDDataActorContext _ddataContext;
        private readonly IReadConsistency _readConsistency;
        private readonly IWriteConsistency _writeConsistency;
        private const int MaxUpdateAttempts = 3;

        // The default maximum-frame-size is 256 KiB with Artery.
        // When using entity identifiers with 36 character strings (e.g. UUID.randomUUID).
        // By splitting the elements over 5 keys we can support 10000 entities per shard.
        // The Gossip message size of 5 ORSet with 2000 ids is around 200 KiB.
        // This is by intention not configurable because it's important to have the same
        // configuration on each node.
        private const int NrOfKeys = 5;

        private readonly ImmutableArray<ORSetKey<EntryId>> _stateKeys;
        private Receive _receive;

        public DDataShard(IActorContext context, IDDataActorContext ddataContext, Action<object> unhandled, string typeName, string shardId, Props entityProps, ClusterShardingSettings settings, ExtractEntityId extractEntityId, ExtractShardId extractShardId, object handOffStopMessage)
            : base(context, unhandled, typeName, shardId, entityProps, settings, extractEntityId, extractShardId, handOffStopMessage)
        {
            _ddataContext = ddataContext;
            _readConsistency = new ReadMajority(settings.TunningParameters.WaitingForStateTimeout, ddataContext.MajorityCap);
            _writeConsistency = new WriteMajority(settings.TunningParameters.UpdatingStateTimeout, ddataContext.MajorityCap);
            _stateKeys = Enumerable.Range(0, NrOfKeys).Select(i => new ORSetKey<EntryId>($"shard-{typeName}-{shardId}-{i}")).ToImmutableArray();

            _receive = WaitingForState(ImmutableHashSet<int>.Empty);
            GetState();
        }

        public IActorRef Replicator => _ddataContext.Replicator;
        public Cluster Node => _ddataContext.Cluster;
        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy => Settings.TunningParameters.EntityRecoveryStrategy == "constant"
            ? EntityRecoveryStrategy.ConstantStrategy(
                _context.System,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyFrequency,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities)
            : EntityRecoveryStrategy.AllStrategy;

        public bool Receive(object message) => _receive(message);

        private ORSetKey<EntryId> Key(EntryId entityId)
        {
            var i = (Math.Abs(entityId.GetHashCode()) % NrOfKeys);
            return _stateKeys[i];
        }

        private void GetState()
        {
            for (int i = 0; i < NrOfKeys; i++)
            {
                Replicator.Tell(Dsl.Get(_stateKeys[i], _readConsistency, i));
            }
        }

        public override void Initialized()
        {
            // would be initialized after recovery completed
        }

        private Receive WaitingForState(ImmutableHashSet<int> gotKeys) => message =>
        {
            void ReceiveOne(int i)
            {
                var newGotKeys = gotKeys.Add(i);
                if (newGotKeys.Count == NrOfKeys)
                    RecoveryCompleted();
                else
                    _receive = WaitingForState(newGotKeys);
            }

            switch (message)
            {
                case GetSuccess success:
                    var i = (int)success.Request;
                    var key = _stateKeys[i];
                    State = new ShardState(State.Entries.Union(success.Get(key).Elements));
                    ReceiveOne(i);
                    break;
                case GetFailure failure:
                    Log.Error("The DDataShard was unable to get an initial state within 'waiting-for-state-timeout': {0}", Settings.TunningParameters.WaitingForStateTimeout);
                    _ddataContext.Stop();
                    break;
                case NotFound notFound:
                    ReceiveOne((int)notFound.Request);
                    break;
                default: _ddataContext.Stash.Stash(); break;
            }

            return true;
        };

        private void RecoveryCompleted()
        {
            RestartRememberedEntities();
            base.Initialized();
            Log.Debug("DDataShard recovery completed shard [{0}] with [{1}] entities", ShardId, State.Entries.Count);
            _ddataContext.Stash.UnstashAll();
            _receive = HandleCommand;
        }

        protected override void ProcessChange<T>(T evt, Action<T> handler)
        {
            _receive = WaitingForUpdate<T>(evt, handler, _receive);
            SendUpdate(evt, retryCount: 1);
        }

        private void SendUpdate(StateChange e, int retryCount)
        {
            Replicator.Tell(Dsl.Update(Key(e.EntityId), ORSet<EntryId>.Empty, _writeConsistency, Tuple.Create(e, retryCount),
                existing =>
                {
                    switch (e)
                    {
                        case EntityStarted started: return existing.Add(Node, started.EntityId);
                        case EntityStopped stopped: return existing.Remove(Node, stopped.EntityId);
                        default: throw new NotSupportedException($"DDataShard send update event not supported: {e}");
                    }
                }));
        }

        private Receive WaitingForUpdate<TEvent>(TEvent e, Action<TEvent> afterUpdateCallback, Receive old) where TEvent : StateChange => message =>
        {
            switch (message)
            {
                case UpdateSuccess success when Equals(((Tuple<StateChange, int>)success.Request).Item1, e):
                    Log.Debug("The DDataShard state was successfully updated with {0}", e);
                    _receive = old;
                    afterUpdateCallback(e);
                    _ddataContext.Stash.UnstashAll();
                    break;
                case UpdateTimeout timeout when Equals(((Tuple<StateChange, int>)timeout.Request).Item1, e):
                    var t = (Tuple<StateChange, int>)timeout.Request;
                    var retryCount = t.Item2;
                    if (retryCount == MaxUpdateAttempts)
                    {
                        // parent ShardRegion supervisor will notice that it terminated and will start it again, after backoff
                        Log.Error("The DDataShard was unable to update state after {0} attempts, within 'updating-state-timeout'={1}, event={2}. " +
                            "Shard will be restarted after backoff.", MaxUpdateAttempts, Settings.TunningParameters.UpdatingStateTimeout, e);
                        _ddataContext.Stop();
                    }
                    else
                    {
                        Log.Error("The DDataShard was unable to update state, attempt {0} of {1}, within 'updating-state-timeout'={2}, event={3}",
                            retryCount, MaxUpdateAttempts, Settings.TunningParameters.UpdatingStateTimeout, e);
                        SendUpdate(e, retryCount + 1);
                    }
                    break;
                case ModifyFailure failure when Equals(((Tuple<StateChange, int>)failure.Request).Item1, e):
                    Log.Error("The DDataShard was unable to update state with error {0} and event {1}. Shard will be restarted", failure.Cause, e);
                    ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                    break;
                default: _ddataContext.Stash.Stash(); break;
            }
            return true;
        };

        private void RestartRememberedEntities()
        {
            foreach (var scheduledRecovery in RememberedEntitiesRecoveryStrategy.RecoverEntities(State.Entries))
            {
                scheduledRecovery.ContinueWith(t => new RestartEntities(t.Result), TaskContinuationOptions.ExecuteSynchronously).PipeTo(_context.Self, _context.Self);
            }
        }
    }
}