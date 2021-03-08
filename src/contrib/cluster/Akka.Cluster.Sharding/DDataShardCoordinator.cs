//-----------------------------------------------------------------------
// <copyright file="DDataShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.DistributedData;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    using static Akka.Cluster.Sharding.ShardCoordinator;
    using ShardId = String;

    /// <summary>
    /// Singleton coordinator (with state based on ddata) that decides where to allocate shards.
    /// </summary>
    internal sealed class DDataShardCoordinator : ActorBase, IWithTimers, IWithUnboundedStash
    {

        private sealed class RememberEntitiesStoreStopped
        {
            public static RememberEntitiesStoreStopped Instance = new RememberEntitiesStoreStopped();

            private RememberEntitiesStoreStopped()
            {
            }
        }

        private sealed class RememberEntitiesTimeout
        {
            public RememberEntitiesTimeout(ShardId shardId)
            {
                ShardId = shardId;
            }

            public string ShardId { get; }
        }

        private sealed class RememberEntitiesLoadTimeout
        {
            public static readonly RememberEntitiesLoadTimeout Instance = new RememberEntitiesLoadTimeout();

            private RememberEntitiesLoadTimeout()
            {
            }
        }

        internal static Props Props(
            string typeName,
            ClusterShardingSettings settings,
            IShardAllocationStrategy allocationStrategy,
            IActorRef replicator,
            int majorityMinCap,
            IRememberEntitiesProvider rememberEntitiesStoreProvider)
        {
            return Actor.Props.Create(() => new DDataShardCoordinator(
                typeName,
                settings,
                allocationStrategy,
                replicator,
                majorityMinCap,
                rememberEntitiesStoreProvider))
                .WithDeploy(Deploy.Local);
        }

        private const string RememberEntitiesTimeoutKey = "RememberEntityTimeout";

        private readonly IActorRef replicator;
        private readonly ShardCoordinator baseImpl;
        private bool VerboseDebug => baseImpl.VerboseDebug;

        private readonly IReadConsistency stateReadConsistency;
        private readonly IWriteConsistency stateWriteConsistency;
        private readonly CoordinatorState initEmptyState;
        private bool terminating = false;
        private UniqueAddress selfUniqueAddress;
        private readonly LWWRegisterKey<ShardCoordinator.CoordinatorState> coordinatorStateKey;
        private ImmutableHashSet<(IActorRef, GetShardHome)> getShardHomeRequests = ImmutableHashSet<(IActorRef, GetShardHome)>.Empty;
        private readonly IActorRef rememberEntitiesStore;
        private bool rememberEntities;

        public ITimerScheduler Timers { get; set; }
        public IStash Stash { get; set; }

        private string TypeName => baseImpl.TypeName;
        private ClusterShardingSettings Settings => baseImpl.Settings;
        private CoordinatorState State { get => baseImpl.State; set => baseImpl.State = value; }
        private ILoggingAdapter Log => baseImpl.Log;

        public DDataShardCoordinator(
            string typeName,
            ClusterShardingSettings settings,
            IShardAllocationStrategy allocationStrategy,
            IActorRef replicator,
            int majorityMinCap,
            IRememberEntitiesProvider rememberEntitiesStoreProvider)
        {
            this.replicator = replicator;
            var log = Context.GetLogger();
            var verboseDebug = Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging");

            baseImpl = new ShardCoordinator(typeName, settings, allocationStrategy,
                Context, log, verboseDebug, Update, UnstashOneGetShardHomeRequest);

            if (settings.TuningParameters.CoordinatorStateReadMajorityPlus == int.MaxValue)
                stateReadConsistency = new ReadAll(settings.TuningParameters.WaitingForStateTimeout);
            else
                stateReadConsistency = new ReadMajority/*Plus*/(settings.TuningParameters.WaitingForStateTimeout/*, additional*/, majorityMinCap);

            if (settings.TuningParameters.CoordinatorStateWriteMajorityPlus == int.MaxValue)
                stateWriteConsistency = new WriteAll(settings.TuningParameters.UpdatingStateTimeout);
            else
                stateWriteConsistency = new WriteMajority/*Plus*/(settings.TuningParameters.UpdatingStateTimeout/*, additional*/, majorityMinCap);

            Cluster node = Cluster.Get(Context.System);
            selfUniqueAddress = node.SelfUniqueAddress;

            coordinatorStateKey = new LWWRegisterKey<CoordinatorState>(typeName + "CoordinatorState");

            initEmptyState = CoordinatorState.Empty.WithRememberEntities(settings.RememberEntities);


            if (rememberEntitiesStoreProvider != null)
            {
                log.Debug("{0}: Starting remember entities store from provider {1}", typeName, rememberEntitiesStoreProvider);
                rememberEntitiesStore = Context.WatchWith(
                    Context.ActorOf(rememberEntitiesStoreProvider.CoordinatorStoreProps(), "RememberEntitiesStore"),
                    RememberEntitiesStoreStopped.Instance);
            }
            rememberEntities = rememberEntitiesStore != null;
            node.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.ClusterShuttingDown));

            // get state from ddata replicator, repeat until GetSuccess
            GetCoordinatorState();
            if (settings.RememberEntities)
                GetAllRememberedShards();

            Context.Become(WaitingForInitialState(ImmutableHashSet<ShardId>.Empty));
        }


        protected override bool Receive(object message)
        {
            throw new IllegalStateException("Default receive never expected to actually be used");
        }

        /// <summary>
        /// This state will drop all other messages since they will be retried
        /// Note remembered entities initial set of shards can arrive here or later, does not keep us in this state
        /// </summary>
        /// <param name="rememberedShards"></param>
        /// <returns></returns>
        private Receive WaitingForInitialState(IImmutableSet<ShardId> rememberedShards)
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case GetSuccess g when g.Key.Equals(coordinatorStateKey):
                        var existingState = g.Get(coordinatorStateKey).Value.WithRememberEntities(Settings.RememberEntities);
                        if (VerboseDebug)
                            Log.Debug("{0}: Received initial coordinator state [{1}]", TypeName, existingState);
                        else
                            Log.Debug(
                                "{0}: Received initial coordinator state with [{1}] shards",
                                TypeName,
                                existingState.Shards.Count + existingState.UnallocatedShards.Count);
                        OnInitialState(existingState, rememberedShards);
                        return true;

                    case GetFailure m when m.Key.Equals(coordinatorStateKey):
                        Log.Error(
                            "{0}: The ShardCoordinator was unable to get an initial state within 'waiting-for-state-timeout': {1} millis (retrying). Has ClusterSharding been started on all nodes?",
                            TypeName,
                            stateReadConsistency.Timeout.TotalMilliseconds);
                        // repeat until GetSuccess
                        GetCoordinatorState();
                        return true;

                    case NotFound m when m.Key.Equals(coordinatorStateKey):
                        Log.Debug("{0}: Initial coordinator is empty.", TypeName);
                        // this.state is empty initially
                        OnInitialState(State, rememberedShards);
                        return true;

                    case RememberEntitiesCoordinatorStore.RememberedShards m:
                        Log.Debug("{0}: Received [{1}] remembered shard ids (when waitingForInitialState)", TypeName, m.Entities.Count);
                        Context.Become(WaitingForInitialState(m.Entities));
                        Timers.Cancel(RememberEntitiesTimeoutKey);
                        return true;

                    case RememberEntitiesLoadTimeout _:
                        // repeat until successful
                        GetAllRememberedShards();
                        return true;

                    case Terminate _:
                        Log.Debug("{0}: Received termination message while waiting for state", TypeName);
                        Context.Stop(Self);
                        return true;

                    case Register m:
                        Log.Debug("{0}: ShardRegion tried to register but ShardCoordinator not initialized yet: [{1}]",
                            TypeName,
                            m.ShardRegion);
                        return true;

                    case RegisterProxy m:
                        Log.Debug("{0}: ShardRegion proxy tried to register but ShardCoordinator not initialized yet: [{1}]",
                            TypeName,
                            m.ShardRegionProxy);
                        return true;
                }
                return ReceiveTerminated(message);
            }

            return Receive;
        }

        private void OnInitialState(CoordinatorState loadedState, IImmutableSet<ShardId> rememberedShards)
        {
            if (Settings.RememberEntities && rememberedShards.Count > 0)
            {
                // Note that we don't wait for shards from store so they could also arrive later
                var newUnallocatedShards = State.UnallocatedShards.Union(rememberedShards.Except(State.Shards.Keys));
                State = loadedState.Copy(unallocatedShards: newUnallocatedShards);
            }
            else
                State = loadedState;

            if (State.IsEmpty)
            {
                // empty state, activate immediately
                Activate();
            }
            else
            {
                Context.Become(WaitingForStateInitialized);
                // note that watchStateActors may call update
                baseImpl.WatchStateActors();
            }
        }

        /// <summary>
        /// this state will stash all messages until it receives StateInitialized,
        /// which was scheduled by previous watchStateActors
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private bool WaitingForStateInitialized(object message)
        {
            switch (message)
            {
                case StateInitialized _:
                    UnstashOneGetShardHomeRequest();
                    Stash.UnstashAll();
                    baseImpl.ReceiveStateInitialized();
                    Activate();
                    return true;

                case GetShardHome g:
                    StashGetShardHomeRequest(Sender, g);
                    return true;

                case Terminate _:
                    Log.Debug("{0}: Received termination message while waiting for state initialized", TypeName);
                    Context.Stop(Self);
                    return true;

                case RememberEntitiesCoordinatorStore.RememberedShards m:
                    Log.Debug("{0}: Received [{1}] remembered shard ids (when waitingForStateInitialized)",
                        TypeName,
                        m.Entities.Count);
                    var newUnallocatedShards = State.UnallocatedShards.Union(m.Entities.Except(State.Shards.Keys));
                    State = State.Copy(unallocatedShards: newUnallocatedShards);
                    Timers.Cancel(RememberEntitiesTimeoutKey);
                    return true;

                case RememberEntitiesLoadTimeout _:
                    // repeat until successful
                    GetAllRememberedShards();
                    return true;

                case RememberEntitiesStoreStopped _:
                    OnRememberEntitiesStoreStopped();
                    return true;

                case var _:
                    Stash.Stash();
                    return true;
            }
        }

        /// <summary>
        /// this state will stash all messages until it receives UpdateSuccess and a successful remember shard started
        /// if remember entities is enabled
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="evt"></param>
        /// <param name="shardId"></param>
        /// <param name="waitingForStateWrite"></param>
        /// <param name="waitingForRememberShard"></param>
        /// <param name="afterUpdateCallback"></param>
        /// <returns></returns>
        private Receive WaitingForUpdate<TEvent>(
            TEvent evt,
            ShardId shardId,
            bool waitingForStateWrite,
            bool waitingForRememberShard,
            Action<TEvent> afterUpdateCallback)
            where TEvent : IDomainEvent
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case UpdateSuccess m when m.Key.Equals(coordinatorStateKey) && m.Request.Equals(evt):
                        if (!waitingForRememberShard)
                        {
                            Log.Debug("{0}: The coordinator state was successfully updated with {1}", TypeName, evt);
                            if (shardId != null)
                                Timers.Cancel(RememberEntitiesTimeoutKey);
                            UnbecomeAfterUpdate(evt, afterUpdateCallback);
                        }
                        else
                        {
                            Log.Debug("{0}: The coordinator state was successfully updated with {1}, waiting for remember shard update",
                                TypeName,
                                evt);
                            Context.Become(
                                WaitingForUpdate(
                                    evt,
                                    shardId,
                                    waitingForStateWrite = false,
                                    waitingForRememberShard = true,
                                    afterUpdateCallback: afterUpdateCallback));
                        }
                        return true;

                    case UpdateTimeout m when m.Key.Equals(coordinatorStateKey) && m.Request.Equals(evt):
                        Log.Error(
                            "{0}: The ShardCoordinator was unable to update a distributed state within 'updating-state-timeout': {1} millis ({2}). " +
                            "Perhaps the ShardRegion has not started on all active nodes yet? event={3}",
                            TypeName,
                            stateWriteConsistency.Timeout.TotalMilliseconds,
                            terminating ? "terminating" : "retrying",
                            evt);
                        if (terminating)
                        {
                            Context.Stop(Self);
                        }
                        else
                        {
                            // repeat until UpdateSuccess
                            SendCoordinatorStateUpdate(evt);
                        }
                        return true;

                    case ModifyFailure m:
                        Log.Error(
                            m.Cause,
                            "{0}: The ShardCoordinator was unable to update a distributed state {1} with error {2} and event {3}. {4}",
                            TypeName,
                            m.Key,
                            m.ErrorMessage,
                            evt,
                            terminating ? "Coordinator will be terminated due to Terminate message received"
                            : "Coordinator will be restarted");
                        if (terminating)
                        {
                            Context.Stop(Self);
                        }
                        else
                        {
                            ExceptionDispatchInfo.Capture(m.Cause).Throw();
                        }
                        return true;

                    case GetShardHome g:
                        if (!baseImpl.HandleGetShardHome(g.Shard))
                            StashGetShardHomeRequest(Sender, g); // must wait for update that is in progress
                        return true;

                    case Terminate _:
                        Log.Debug("{0}: The ShardCoordinator received termination message while waiting for update", TypeName);
                        terminating = true;
                        Stash.Stash();
                        return true;

                    case RememberEntitiesCoordinatorStore.UpdateDone m:
                        if (!shardId.Contains(m.ShardId))
                        {
                            Log.Warning("{0}: Saw remember entities update complete for shard id [{1}], while waiting for [{2}]",
                                TypeName,
                                m.ShardId,
                                shardId ?? "");
                        }
                        else
                        {
                            if (!waitingForStateWrite)
                            {
                                Log.Debug("{0}: The ShardCoordinator saw remember shard start successfully written {1}", TypeName, evt);
                                if (shardId != null)
                                    Timers.Cancel(RememberEntitiesTimeoutKey);
                                UnbecomeAfterUpdate(evt, afterUpdateCallback);
                            }
                            else
                            {
                                Log.Debug("{0}: The ShardCoordinator saw remember shard start successfully written {1}, waiting for state update",
                                    TypeName,
                                    evt);
                                Context.Become(
                                    WaitingForUpdate(
                                        evt,
                                        shardId,
                                        waitingForStateWrite = true,
                                        waitingForRememberShard = false,
                                        afterUpdateCallback: afterUpdateCallback));
                            }
                        }
                        return true;

                    case RememberEntitiesCoordinatorStore.UpdateFailed m:
                        if (shardId.Contains(m.ShardId))
                        {
                            OnRememberEntitiesUpdateFailed(m.ShardId);
                        }
                        else
                        {
                            Log.Warning("{0}: Got an remember entities update failed for [{1}] while waiting for [{2}], ignoring",
                                TypeName,
                                m.ShardId,
                                shardId ?? "");
                        }
                        return true;

                    case RememberEntitiesTimeout m:
                        if (shardId.Contains(m.ShardId))
                        {
                            OnRememberEntitiesUpdateFailed(m.ShardId);
                        }
                        else
                        {
                            Log.Warning("{0}: Got an remember entities update timeout for [{1}] while waiting for [{2}], ignoring",
                                TypeName,
                                m.ShardId,
                                shardId ?? "");
                        }
                        return true;

                    case RememberEntitiesStoreStopped _:
                        OnRememberEntitiesStoreStopped();
                        return true;

                    case RememberEntitiesCoordinatorStore.RememberedShards _:
                        Log.Debug("{0}: Late arrival of remembered shards while waiting for update, stashing", TypeName);
                        Stash.Stash();
                        return true;

                    case var _:
                        Stash.Stash();
                        return true;
                }
            }

            return Receive;
        }

        private void UnbecomeAfterUpdate<TEvent>(TEvent evt, Action<TEvent> afterUpdateCallback) where TEvent : IDomainEvent
        {
            Context.UnbecomeStacked();
            afterUpdateCallback(evt);
            if (VerboseDebug)
                Log.Debug("{0}: New coordinator state after [{1}]: [{2}]", TypeName, evt, State);
            UnstashOneGetShardHomeRequest();
            Stash.UnstashAll();
        }

        private void StashGetShardHomeRequest(IActorRef sender, GetShardHome request)
        {
            Log.Debug(
              "{0}: GetShardHome [{1}] request from [{2}] stashed, because waiting for initial state or update of state. " +
              "It will be handled afterwards.",
              TypeName,
              request.Shard,
              sender);
            getShardHomeRequests = getShardHomeRequests.Add((sender, request));
        }

        private void UnstashOneGetShardHomeRequest()
        {
            if (getShardHomeRequests.Count > 0)
            {
                // unstash one, will continue unstash of next after receive GetShardHome or update completed
                var requestTuple = getShardHomeRequests.First();
                var (originalSender, request) = requestTuple;
                Self.Tell(request, sender: originalSender);
                getShardHomeRequests = getShardHomeRequests.Remove(requestTuple);
            }
        }

        private void Activate()
        {
            Context.Become(msg => baseImpl.Active(msg) || ReceiveLateRememberedEntities(msg));
            Log.Info("{0}: ShardCoordinator was moved to the active state with [{1}] shards", TypeName, State.Shards.Count);
            if (VerboseDebug)
                Log.Debug("{0}: Full ShardCoordinator initial state {1}", TypeName, State);
        }

        /// <summary>
        /// only used once the coordinator is initialized
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private bool ReceiveLateRememberedEntities(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.RememberedShards m:
                    Log.Debug("{0}: Received [{1}] remembered shard ids (after state initialized)", TypeName, m.Entities.Count);
                    if (m.Entities.Count > 0)
                    {
                        var newUnallocatedShards = State.UnallocatedShards.Union(m.Entities.Except(State.Shards.Keys));
                        State = State.Copy(unallocatedShards: newUnallocatedShards);
                        baseImpl.AllocateShardHomesForRememberEntities();
                    }
                    Timers.Cancel(RememberEntitiesTimeoutKey);
                    break;

                case RememberEntitiesLoadTimeout _:
                    // repeat until successful
                    GetAllRememberedShards();
                    break;
            }
            return false;
        }

        private void Update<TEvent>(TEvent evt, Action<TEvent> handler) where TEvent : IDomainEvent
        {
            SendCoordinatorStateUpdate(evt);
            Receive waitingReceive;
            switch (evt)
            {
                case ShardHomeAllocated s when rememberEntities && !State.Shards.ContainsKey(s.Shard):
                    RememberShardAllocated(s.Shard);
                    waitingReceive = WaitingForUpdate(
                        evt,
                        shardId: s.Shard,
                        waitingForStateWrite: true,
                        waitingForRememberShard: true,
                        afterUpdateCallback: handler);
                    break;
                case var _:
                    // no update of shards, already known
                    waitingReceive = WaitingForUpdate(
                        evt,
                        shardId: null,
                        waitingForStateWrite: true,
                        waitingForRememberShard: false,
                        afterUpdateCallback: handler);
                    break;
            }
            Context.BecomeStacked(waitingReceive);
        }

        private void GetCoordinatorState()
        {
            replicator.Tell(Dsl.Get(coordinatorStateKey, stateReadConsistency));
        }

        private void GetAllRememberedShards()
        {
            Timers.StartSingleTimer(
                RememberEntitiesTimeoutKey,
                RememberEntitiesLoadTimeout.Instance,
                Settings.TuningParameters.WaitingForStateTimeout);
            if (rememberEntitiesStore != null)
                rememberEntitiesStore.Tell(RememberEntitiesCoordinatorStore.GetShards.Instance);
        }

        private void SendCoordinatorStateUpdate(IDomainEvent evt)
        {
            var s = State.Updated(evt);
            if (VerboseDebug)
                Log.Debug("{0}: Storing new coordinator state [{1}]", TypeName, State);
            replicator.Tell(Dsl.Update(
                coordinatorStateKey,
                new LWWRegister<CoordinatorState>(selfUniqueAddress, initEmptyState),
                stateWriteConsistency,
                evt,
                reg => reg.WithValue(selfUniqueAddress, s)));
        }

        private void RememberShardAllocated(string newShard)
        {
            Log.Debug("{0}: Remembering shard allocation [{1}]", TypeName, newShard);
            if (rememberEntitiesStore != null)
                rememberEntitiesStore.Tell(new RememberEntitiesCoordinatorStore.AddShard(newShard));
            Timers.StartSingleTimer(
                RememberEntitiesTimeoutKey,
                new RememberEntitiesTimeout(newShard),
                Settings.TuningParameters.UpdatingStateTimeout);
        }

        private bool ReceiveTerminated(object message)
        {
            if (!baseImpl.ReceiveTerminated(message))
            {
                if (message is RememberEntitiesStoreStopped)
                {
                    OnRememberEntitiesStoreStopped();
                    return true;
                }
            }
            return false;
        }

        private void OnRememberEntitiesUpdateFailed(ShardId shardId)
        {
            Log.Error("{0}: The ShardCoordinator was unable to update remembered shard [{1}] within 'updating-state-timeout': {2} millis, {3}",
              TypeName,
              shardId,
              Settings.TuningParameters.UpdatingStateTimeout,
              terminating ? "terminating" : "retrying");
            if (terminating)
                Context.Stop(Self);
            else
            {
                // retry until successful
                RememberShardAllocated(shardId);
            }
        }

        private void OnRememberEntitiesStoreStopped()
        {
            // rely on backoff supervision of coordinator
            Log.Error("{0}: The ShardCoordinator stopping because the remember entities store stopped", TypeName);
            Context.Stop(Self);
        }

        protected override void PreStart()
        {
            baseImpl.PreStart();
        }

        protected override void PostStop()
        {
            base.PostStop();
            baseImpl.PostStop();
        }
    }
}
