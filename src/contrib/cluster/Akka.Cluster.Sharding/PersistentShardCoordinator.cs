//-----------------------------------------------------------------------
// <copyright file="PersistentShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
{
    using static Akka.Cluster.Sharding.ShardCoordinator;
    using ShardId = String;

    /// <summary>
    /// Singleton coordinator that decides where to allocate shards.
    ///
    /// Users can migrate to using DData to store state then either event sourcing or ddata to store
    /// the remembered entities.
    /// </summary>
    internal sealed class PersistentShardCoordinator : PersistentActor
    {
        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="PersistentShardCoordinator"/> actor.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="allocationStrategy">TBD</param>
        /// <returns>TBD</returns>
        internal static Props Props(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy)
        {
            return Actor.Props.Create(() => new PersistentShardCoordinator(typeName, settings, allocationStrategy))
                .WithDeploy(Deploy.Local);
        }

        private readonly ShardCoordinator baseImpl;

        private bool VerboseDebug => baseImpl.VerboseDebug;
        private string TypeName => baseImpl.TypeName;
        private ClusterShardingSettings Settings => baseImpl.Settings;
        private CoordinatorState State { get => baseImpl.State; set => baseImpl.State = value; }

        public PersistentShardCoordinator(
            string typeName,
            ClusterShardingSettings settings,
            IShardAllocationStrategy allocationStrategy
            )
        {
            var log = Context.GetLogger();
            var verboseDebug = Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging");

            baseImpl = new ShardCoordinator(typeName, settings, allocationStrategy,
                Context, log, verboseDebug, Update, UnstashOneGetShardHomeRequest);

            //should have been this: $"/sharding/{typeName}Coordinator";
            PersistenceId = $"/system/sharding/{typeName}Coordinator/singleton/coordinator"; //used for backward compatibility instead of Self.Path.ToStringWithoutAddress()

            JournalPluginId = Settings.JournalPluginId;
            SnapshotPluginId = Settings.SnapshotPluginId;
        }

        public override string PersistenceId { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker _:
                case SnapshotOffer offer when offer.Snapshot is EventSourcedRememberEntitiesCoordinatorStore.State _:
                    throw new IllegalStateException(
                      "state-store is set to persistence but a migration has taken place to remember-entities-store=eventsourced. You can not downgrade.");

                case IDomainEvent evt:
                    if (VerboseDebug)
                        Log.Debug("{0}: receiveRecover {1}", TypeName, evt);

                    switch (evt)
                    {
                        case ShardRegionRegistered _:
                            State = State.Updated(evt);
                            return true;
                        case ShardRegionProxyRegistered _:
                            State = State.Updated(evt);
                            return true;
                        case ShardRegionTerminated regionTerminated:
                            if (State.Regions.ContainsKey(regionTerminated.Region))
                                State = State.Updated(evt);
                            else
                                //Log.Debug(
                                //  "{0}: ShardRegionTerminated, but region {1} was not registered. This inconsistency is due to that " +
                                //  " some stored ActorRef in Akka v2.3.0 and v2.3.1 did not contain full address information. It will be " +
                                //  "removed by later watch.",
                                //  typeName,
                                //  regionTerminated.Region);
                                Log.Debug("{0}: ShardRegionTerminated but region {1} was not registered", TypeName, regionTerminated.Region);
                            return true;
                        case ShardRegionProxyTerminated proxyTerminated:
                            if (State.RegionProxies.Contains(proxyTerminated.RegionProxy))
                                State = State.Updated(evt);
                            return true;
                        case ShardHomeAllocated _:
                            State = State.Updated(evt);
                            return true;
                        case ShardHomeDeallocated _:
                            State = State.Updated(evt);
                            return true;
                        case ShardCoordinatorInitialized _:
                            // not used here
                            return true;
                    }
                    return false;
                case SnapshotOffer offer when offer.Snapshot is CoordinatorState state:
                    if (VerboseDebug)
                        Log.Debug("{0}: receiveRecover SnapshotOffer {1}", TypeName, state);
                    State = state.WithRememberEntities(Settings.RememberEntities);
                    // Old versions of the state object may not have unallocatedShard set,
                    // thus it will be null.
                    if (state.UnallocatedShards == null)
                        State = State.Copy(unallocatedShards: ImmutableHashSet<ShardId>.Empty);
                    return true;

                case RecoveryCompleted _:
                    State = State.WithRememberEntities(Settings.RememberEntities);
                    baseImpl.WatchStateActors();
                    return true;
            }
            return false;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool ReceiveCommand(object message)
        {
            return WaitingForStateInitialized(message);
        }

        private bool WaitingForStateInitialized(object message)
        {
            switch (message)
            {
                case Terminate _:
                    Log.Debug("{0}: Received termination message before state was initialized", TypeName);
                    Context.Stop(Self);
                    return true;

                case StateInitialized _:
                    baseImpl.ReceiveStateInitialized();
                    Log.Debug("{0}: Coordinator initialization completed", TypeName);
                    Context.Become(msg => baseImpl.Active(msg) || ReceiveSnapshotResult(msg));
                    return true;

                case Register r:
                    // region will retry so ok to ignore
                    Log.Debug("{0}: Ignoring registration from region [{1}] while initializing", TypeName, r.ShardRegion);
                    return true;
            }

            if (baseImpl.ReceiveTerminated(message)) return true;
            else return ReceiveSnapshotResult(message);
        }

        private bool ReceiveSnapshotResult(object message)
        {
            switch (message)
            {
                case SaveSnapshotSuccess m:
                    Log.Debug("{0}: Persistent snapshot to [{1}] saved successfully", TypeName, m.Metadata.SequenceNr);
                    InternalDeleteMessagesBeforeSnapshot(m, Settings.TuningParameters.KeepNrOfBatches, Settings.TuningParameters.SnapshotAfter);
                    return true;

                case SaveSnapshotFailure m:
                    Log.Warning(m.Cause, "{0}: Persistent snapshot failure: {1}", TypeName, m.Cause.Message);
                    return true;

                case DeleteMessagesSuccess m:
                    Log.Debug("{0}: Persistent messages to [{1}] deleted successfully", TypeName, m.ToSequenceNr);
                    DeleteSnapshots(new SnapshotSelectionCriteria(m.ToSequenceNr - 1));
                    return true;

                case DeleteMessagesFailure m:
                    Log.Warning(m.Cause, "{0}: Persistent messages to [{1}] deletion failure: {2}", TypeName, m.ToSequenceNr, m.Cause.Message);
                    return true;

                case DeleteSnapshotsSuccess m:
                    Log.Debug("{0}: Persistent snapshots to [{1}] deleted successfully", TypeName, m.Criteria.MaxSequenceNr);
                    return true;

                case DeleteSnapshotsFailure m:
                    Log.Warning(m.Cause, "{0}: Persistent snapshots to [{1}] deletion failure: {2}", TypeName, m.Criteria.MaxSequenceNr, m.Cause);
                    return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="e">TBD</param>
        /// <param name="handler">TBD</param>
        /// <returns>TBD</returns>
        private void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : IDomainEvent
        {
            SaveSnapshotWhenNeeded();
            Persist(e, handler);
        }

        private void SaveSnapshotWhenNeeded()
        {
            if (LastSequenceNr % Settings.TuningParameters.SnapshotAfter == 0 && LastSequenceNr != 0)
            {
                Log.Debug("{0}: Saving snapshot, sequence number [{1}]", TypeName, SnapshotSequenceNr);
                SaveSnapshot(State);
            }
        }

        private void UnstashOneGetShardHomeRequest()
        {
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
