//-----------------------------------------------------------------------
// <copyright file="DDataShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    internal sealed class DDataShardCoordinator : ActorBase, IShardCoordinator, IWithUnboundedStash
    {
        internal static Props Props(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy, IActorRef replicator, int majorityMinCap, bool rememberEntities) => 
            Actor.Props.Create(() => new DDataShardCoordinator(typeName, settings, allocationStrategy, replicator, majorityMinCap, rememberEntities)).WithDeploy(Deploy.Local);

        public PersistentShardCoordinator.State CurrentState { get; set; }
        public ClusterShardingSettings Settings { get; }
        public IShardAllocationStrategy AllocationStrategy { get; }
        public ICancelable RebalanceTask { get; }
        public Cluster Cluster { get; }
        IActorContext IShardCoordinator.Context => Context;
        IActorRef IShardCoordinator.Self => Self;
        IActorRef IShardCoordinator.Sender => Sender;
        public ILoggingAdapter Log { get; }
        public ImmutableDictionary<string, ICancelable> UnAckedHostShards { get; set; } = ImmutableDictionary<string, ICancelable>.Empty;
        public ImmutableDictionary<string, ImmutableHashSet<IActorRef>> RebalanceInProgress { get; set; } = ImmutableDictionary<string, ImmutableHashSet<IActorRef>>.Empty;
        public ImmutableHashSet<IActorRef> GracefullShutdownInProgress { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableHashSet<IActorRef> AliveRegions { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableHashSet<IActorRef> RegionTerminationInProgress { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public TimeSpan RemovalMargin { get; }
        public IStash Stash { get; set; }
        public int MinMembers { get; }

        private readonly IReadConsistency _readConsistency;
        private readonly IWriteConsistency _writeConsistency;
        private readonly LWWRegisterKey<PersistentShardCoordinator.State> _coordinatorStateKey;
        private readonly GSetKey<string> _allShardsKey;
        private readonly IActorRef _replicator;
        private readonly bool _rememberEntities;

        private bool _allRegionsRegistered = false;
        private ImmutableHashSet<IKey<IReplicatedData>> _allKeys;
        private IImmutableSet<string> _shards = ImmutableHashSet<string>.Empty;
        private bool _terminating = false;

        public DDataShardCoordinator(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy, IActorRef replicator, int majorityMinCap, bool rememberEntities)
        {
            _replicator = replicator;
            _rememberEntities = rememberEntities;
            Settings = settings;
            AllocationStrategy = allocationStrategy;
            Log = Context.GetLogger();
            Cluster = Cluster.Get(Context.System);
            CurrentState = PersistentShardCoordinator.State.Empty.WithRememberEntities(settings.RememberEntities);
            RemovalMargin = Cluster.DowningProvider.DownRemovalMargin;
            MinMembers = string.IsNullOrEmpty(settings.Role)
                ? Cluster.Settings.MinNrOfMembers
                : Cluster.Settings.MinNrOfMembersOfRole.GetValueOrDefault(settings.Role, 1);
            RebalanceTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.TunningParameters.RebalanceInterval, Settings.TunningParameters.RebalanceInterval, Self, RebalanceTick.Instance, Self);

            _readConsistency = new ReadMajority(settings.TunningParameters.WaitingForStateTimeout, majorityMinCap);
            _writeConsistency = new WriteMajority(settings.TunningParameters.UpdatingStateTimeout, majorityMinCap);
            _coordinatorStateKey = new LWWRegisterKey<PersistentShardCoordinator.State>(typeName + "CoordinatorState");
            _allShardsKey = new GSetKey<string>($"shard-{typeName}-all");
            _allKeys = rememberEntities
                ? ImmutableHashSet.CreateRange(new IKey<IReplicatedData>[] { _coordinatorStateKey, _allShardsKey })
                : ImmutableHashSet.Create<IKey<IReplicatedData>>(_coordinatorStateKey);

            if (rememberEntities)
                replicator.Tell(Dsl.Subscribe(_allShardsKey, Self));

            Cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.ClusterShuttingDown));

            // get state from ddata replicator, repeat until GetSuccess
            GetCoordinatorState();
            GetAllShards();

            Context.Become(WaitingForState(_allKeys));
        }

        protected override bool Receive(object message) => throw new NotImplementedException(); // should never be called

        public bool HasAllRegionsRegistered()
        {
            // the check is only for startup, i.e. once all have registered we don't check more
            if (_allRegionsRegistered)
                return true;
            else
            {
                _allRegionsRegistered = AliveRegions.Count >= MinMembers;
                return _allRegionsRegistered;
            }
        }

        // This state will drop all other messages since they will be retried
        private Receive WaitingForState(ImmutableHashSet<IKey<IReplicatedData>> remainingKeys) => message =>
        {
            switch (message)
            {
                case GetSuccess success when _coordinatorStateKey.Equals(success.Key):
                    {
                        CurrentState = success.Get(_coordinatorStateKey).Value
                            .WithRememberEntities(Settings.RememberEntities);
                        var newRemaining = remainingKeys.Remove(_coordinatorStateKey);
                        if (newRemaining.IsEmpty)
                            BecomeWaitingForStateInitialized();
                        else
                            Context.Become(WaitingForState(newRemaining));
                        return true;
                    }
                case GetFailure failure when _coordinatorStateKey.Equals(failure.Key):
                    {
                        Log.Error("The ShardCoordinator was unable to get an initial state within 'waiting-for-state-timeout': {0} (retrying)", _readConsistency.Timeout);
                        GetCoordinatorState(); // repeat until GetSuccess
                        return true;
                    }
                case NotFound notFound when _coordinatorStateKey.Equals(notFound.Key):
                    {
                        var newRemaining = remainingKeys.Remove(_coordinatorStateKey);
                        if (newRemaining.IsEmpty)
                            BecomeWaitingForStateInitialized();
                        else
                            Context.Become(WaitingForState(newRemaining));
                        return true;
                    }
                case GetSuccess success when _allShardsKey.Equals(success.Key):
                    {
                        var shards = success.Get(_allShardsKey).Elements;
                        var newUnallocatedShards = CurrentState.UnallocatedShards.Union(shards.Except(CurrentState.Shards.Keys));
                        CurrentState = CurrentState.Copy(unallocatedShards: newUnallocatedShards);
                        var newRemainingKeys = remainingKeys.Remove(_allShardsKey);
                        if (newRemainingKeys.IsEmpty)
                            BecomeWaitingForStateInitialized();
                        else
                            Context.Become(WaitingForState(newRemainingKeys));
                        return true;
                    }
                case GetFailure failure when _allShardsKey.Equals(failure.Key):
                    {
                        Log.Error("The ShardCoordinator was unable to get all shards state within 'waiting-for-state-timeout': {0} (retrying)", _readConsistency.Timeout);
                        // repeat until GetSuccess
                        GetAllShards();
                        return true;
                    }
                case NotFound notFound when _allShardsKey.Equals(notFound.Key):
                    {
                        var newRemainingKeys = remainingKeys.Remove(_allShardsKey);
                        if (newRemainingKeys.IsEmpty)
                            BecomeWaitingForStateInitialized();
                        else
                            Context.Become(WaitingForState(newRemainingKeys));
                        return true;
                    }
                case Terminate _:
                    Log.Debug("Received termination message while waiting for state");
                    Context.Stop(Self);
                    return true;

                default: return this.ReceiveTerminated(message);
            }
        };

        private void BecomeWaitingForStateInitialized()
        {
            if (CurrentState.IsEmpty)
                Activate(); // empty state, activate immediately
            else
            {
                Context.Become(WaitingForStateInitialized);
                // note that watchStateActors may call update
                this.WatchStateActors();
            }
        }

        // this state will stash all messages until it receives StateInitialized,
        // which was scheduled by previous watchStateActors
        private bool WaitingForStateInitialized(object message)
        {
            switch (message)
            {
                case PersistentShardCoordinator.StateInitialized _:
                    Stash.UnstashAll();
                    this.StateInitialized();
                    Activate();
                    return true;
                case Terminate _:
                    Log.Debug("Received termination message while waiting for state initialized");
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }

        private Receive WaitingForUpdate<TEvent>(TEvent e, Action<TEvent> afterUpdateCallback, ImmutableHashSet<IKey<IReplicatedData>> remainingKeys) where TEvent : PersistentShardCoordinator.IDomainEvent => message =>
            {
                switch (message)
                {
                    case UpdateSuccess success when success.Key.Equals(_coordinatorStateKey) && success.Request.Equals(e):
                        Log.Debug("The coordinator state was successfully updated with {0}", e);
                        var newRemainingKeys = remainingKeys.Remove(_coordinatorStateKey);
                        if (newRemainingKeys.IsEmpty)
                            UnbecomeAfterUpdate(e, afterUpdateCallback);
                        else 
                            Context.Become(WaitingForUpdate(e, afterUpdateCallback, newRemainingKeys));
                        return true;

                    case UpdateTimeout timeout when timeout.Key.Equals(_coordinatorStateKey) && timeout.Request.Equals(e):
                        Log.Error("The ShardCoordinator was unable to update a distributed state within 'updating-state-timeout': {0} ({1}), event={2}", _writeConsistency.Timeout, _terminating ? "terminating" : "retrying", e);

                        if (_terminating)
                        {
                            Context.Stop(Self);
                        }
                        else
                        {
                            // repeat until UpdateSuccess
                            SendCoordinatorStateUpdate(e);
                        }

                        return true;

                    case UpdateSuccess success when success.Key.Equals(_allShardsKey) && success.Request is string newShard:
                        Log.Debug("The coordinator shards state was successfully updated with {0}", newShard);
                        var newRemaining = remainingKeys.Remove(_allShardsKey);
                        if (newRemaining.IsEmpty)
                            UnbecomeAfterUpdate(e, afterUpdateCallback);
                        else
                            Context.Become(WaitingForUpdate(e, afterUpdateCallback, newRemaining));
                        return true;

                    case UpdateTimeout timeout when timeout.Key.Equals(_allShardsKey) && timeout.Request is string newShard:
                        Log.Error("The ShardCoordinator was unable to update shards distributed state within 'updating-state-timeout': {0} ({1}), event={2}", _writeConsistency.Timeout, _terminating ? "terminating" : "retrying", e);

                        if (_terminating)
                        {
                            Context.Stop(Self);
                        }
                        else
                        {
                            // repeat until UpdateSuccess
                            SendShardsUpdate(newShard);
                        }

                        return true;

                    case ModifyFailure failure:
                        Log.Error("The ShardCoordinator was unable to update a distributed state {0} with error {1} and event {2}. {3}", failure.Key, failure.Cause, e, _terminating ? "Coordinator will be terminated due to Terminate message received" : "Coordinator will be restarted");

                        if (_terminating)
                        {
                            Context.Stop(Self);
                        }
                        else
                        {
                            ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                        }

                        return true;

                    case PersistentShardCoordinator.GetShardHome getShardHome:
                        if (!this.HandleGetShardHome(getShardHome)) 
                            Stash.Stash();
                        return true;

                    case Terminate _:
                        Log.Debug("Received termination message while waiting for update");
                        _terminating = true;
                        Stash.Stash();
                        return true;

                    default:
                        Stash.Stash();
                        return true;
                }
            };

        private void UnbecomeAfterUpdate<TEvent>(TEvent e, Action<TEvent> afterUpdateCallback)
        {
            Context.UnbecomeStacked();
            afterUpdateCallback(e);
            Stash.UnstashAll();
        }

        private void Activate()
        {
            Context.Become(this.Active);
            Log.Info("Sharding Coordinator was moved to the active state {0}", CurrentState);
        }

        private bool Active(object message)
        {
            if (_rememberEntities && message is Changed changed && changed.Key.Equals(_allShardsKey))
            {
                _shards = changed.Get(_allShardsKey).Elements;
                return true;
            }
            else return ShardCoordinator.Active(this, message);
        }

        public void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : PersistentShardCoordinator.IDomainEvent
        {
            SendCoordinatorStateUpdate(e);
            switch ((PersistentShardCoordinator.IDomainEvent)e)
            {
                case PersistentShardCoordinator.ShardHomeAllocated allocated when _rememberEntities && !_shards.Contains(allocated.Shard):
                    SendShardsUpdate(allocated.Shard);
                    Context.BecomeStacked(WaitingForUpdate(e, handler, _allKeys));
                    break;
                default:
                    // no update of shards, already known
                    Context.BecomeStacked(WaitingForUpdate(e, handler, ImmutableHashSet.Create<IKey<IReplicatedData>>(_coordinatorStateKey)));
                    break;
            }
        }

        private void SendShardsUpdate(string newShard)
        {
            _replicator.Tell(Dsl.Update(_allShardsKey, GSet<string>.Empty, _writeConsistency, newShard, set => set.Add(newShard)));
        }

        private void SendCoordinatorStateUpdate(PersistentShardCoordinator.IDomainEvent e)
        {
            var s = CurrentState.Updated(e);
            _replicator.Tell(Dsl.Update(_coordinatorStateKey,
                new LWWRegister<PersistentShardCoordinator.State>(Cluster.SelfUniqueAddress, PersistentShardCoordinator.State.Empty),
                _writeConsistency,
                e,
                reg => reg.WithValue(Cluster.SelfUniqueAddress, s)));
        }

        private void GetAllShards()
        {
            if (_rememberEntities)
                _replicator.Tell(Dsl.Get(_allShardsKey, _readConsistency));
        }

        private void GetCoordinatorState() => _replicator.Tell(Dsl.Get(_coordinatorStateKey, _readConsistency));
    }
}
