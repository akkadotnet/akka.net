//-----------------------------------------------------------------------
// <copyright file="PersistentView.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Newtonsoft.Json;

namespace Akka.Persistence
{
    /// <summary>
    /// Instructs a <see cref="PersistentView"/> to update itself. This will run a single incremental message replay 
    /// with all messages from the corresponding persistent id's journal that have not yet been consumed by the view.  
    /// To update a view with messages that have been written after handling this request, another <see cref="Update"/> 
    /// request must be sent to the view.
    /// </summary>
    [Serializable]
    public sealed class Update
    {
        public Update()
        {
            IsAwait = false;
            ReplayMax = long.MaxValue;
        }

        public Update(bool isAwait)
        {
            IsAwait = isAwait;
            ReplayMax = long.MaxValue;
        }

        [JsonConstructor]
        public Update(bool isAwait, long replayMax)
        {
            IsAwait = isAwait;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// If `true`, processing of further messages sent to the view will be delayed 
        /// until the incremental message replay, triggered by this update request, completes. 
        /// If `false`, any message sent to the view may interleave with replayed <see cref="Persistent"/> message stream.
        /// </summary>
        public bool IsAwait { get; private set; }

        /// <summary>
        /// Maximum number of messages to replay when handling this update request. Defaults to <see cref="long.MaxValue"/> (i.e. no limit).
        /// </summary>
        public long ReplayMax { get; private set; }
    }

    [Serializable]
    public sealed class ScheduledUpdate
    {
        public ScheduledUpdate(long replayMax)
        {
            ReplayMax = replayMax;
        }

        public long ReplayMax { get; private set; }
    }

    /// <summary>
    /// A view replicates the persistent message stream of a <see cref="PersistentActor"/>. Implementation classes receive
    /// the message stream directly from the Journal. These messages can be processed to update internal state
    /// in order to maintain an (eventual consistent) view of the state of the corresponding persistent actor. A
    /// persistent view can also run on a different node, provided that a replicated journal is used.
    /// 
    /// Implementation classes refer to a persistent actors' message stream by implementing `persistenceId`
    /// with the corresponding (shared) identifier value.
    /// 
    /// Views can also store snapshots of internal state by calling [[autoUpdate]]. The snapshots of a view
    /// are independent of those of the referenced persistent actor. During recovery, a saved snapshot is offered
    /// to the view with a <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger
    /// than the snapshot. Default is to offer the latest saved snapshot.
    /// 
    /// By default, a view automatically updates itself with an interval returned by `autoUpdateInterval`.
    /// This method can be overridden by implementation classes to define a view instance-specific update
    /// interval. The default update interval for all views of an actor system can be configured with the
    /// `akka.persistence.view.auto-update-interval` configuration key. Applications may trigger additional
    /// view updates by sending the view <see cref="Update"/> requests. See also methods
    /// </summary>
    public abstract partial class PersistentView : ActorBase, ISnapshotter, IPersistentIdentity, IWithUnboundedStash
    {
        protected readonly PersistenceExtension Extension;

        private readonly PersistenceSettings.ViewSettings _viewSettings;
        private IActorRef _snapshotStore;
        private IActorRef _journal;

        private ICancelable _scheduleCancellation;

        private IStash _internalStash;
        private ViewState _currentState;

        protected Guid StateId { get { return _currentState.StateId; } }

        protected PersistentView()
        {
            LastSequenceNr = 0L;
            Extension = Persistence.Instance.Apply(Context.System);
            _viewSettings = Extension.Settings.View;
            _internalStash = CreateStash();
            _currentState = RecoveryPending();
        }

        public string JournalPluginId { get; protected set; }

        public string SnapshotPluginId { get; protected set; }

        public IActorRef Journal
        {
            get { return _journal ?? (_journal = Extension.JournalFor(JournalPluginId)); }
        }

        public IActorRef SnapshotStore
        {
            get { return _snapshotStore ?? (_snapshotStore = Extension.SnapshotStoreFor(SnapshotPluginId)); }
        }

        /// <summary>
        /// Used as identifier for snapshots performed by this <see cref="PersistentView"/>. This allows the View to keep 
        /// separate snapshots of data than the <see cref="PersistentActor"/> originating the message stream.
        /// 
        /// The usual case is to have a different identifiers for <see cref="ViewId"/> and <see cref="PersistenceId"/>.
        /// </summary>
        public abstract string ViewId { get; }

        /// <summary>
        /// Id of the persistent entity for which messages should be replayed.
        /// </summary>
        public abstract string PersistenceId { get; }

        /// <summary>
        /// Gets the <see cref="ViewId"/>.
        /// </summary>
        public string SnapshotterId { get { return ViewId; } }

        /// <summary>
        /// If true, the currently processed message was persisted - it sent from the <see cref="Journal"/>.
        /// If false, the currently processed message comes from another actor ('/user/*' path).
        /// </summary>
        public bool IsPersistent { get { return _currentState.IsRecoveryRunning; } }

        /// <summary>
        /// If true, this view will update itself automatically within an interval specified by <see cref="AutoUpdateInterval"/>.
        /// If false, application must update this view explicitly with <see cref="Update"/> requests.
        /// </summary>
        public virtual bool IsAutoUpdate { get { return _viewSettings.AutoUpdate; } }

        /// <summary>
        /// Time interval to automatic updates. Used only when <see cref="IsAutoUpdate"/> value is true.
        /// </summary>
        public virtual TimeSpan AutoUpdateInterval { get { return _viewSettings.AutoUpdateInterval; } }

        /// <summary>
        /// The maximum number of messages to replay per update.
        /// </summary>
        public virtual long AutoUpdateReplayMax { get { return _viewSettings.AutoUpdateReplayMax; } }

        /// <summary>
        /// Highest received sequence number so far or 0 it none persistent event has been replayed yet.
        /// </summary>
        public long LastSequenceNr { get; private set; }

        public IStash Stash { get; set; }

        /// <summary>
        /// Gets last sequence number.
        /// </summary>
        public long SnapshotSequenceNr { get { return LastSequenceNr; } }

        private void ChangeState(ViewState state)
        {
            _currentState = state;
        }

        private void UpdateLastSequenceNr(IPersistentRepresentation persistent)
        {
            if (persistent.SequenceNr > LastSequenceNr) LastSequenceNr = persistent.SequenceNr;
        }

        /// <summary>
        /// Orders to load a snapshots related to persistent actor identified by <paramref name="persistenceId"/>
        /// that match specified <paramref name="criteria"/> up to provided <paramref name="toSequenceNr"/> upper, inclusive bound.
        /// </summary>
        public void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            SnapshotStore.Tell(new LoadSnapshot(persistenceId, criteria, toSequenceNr));
        }

        /// <summary>
        /// Saves a <paramref name="snapshot"/> of this actor's state. If snapshot succeeds, this actor will
        /// receive a <see cref="SaveSnapshotSuccess"/>, otherwise a <see cref="SaveSnapshotFailure"/> message.
        /// </summary>
        public void SaveSnapshot(object snapshot)
        {
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(SnapshotterId, SnapshotSequenceNr), snapshot));
        }

        /// <summary>
        /// Deletes a snapshot identified by <paramref name="sequenceNr"/> and <paramref name="timestamp"/>.
        /// </summary>
        public void DeleteSnapshot(long sequenceNr, DateTime timestamp)
        {
            SnapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(SnapshotterId, sequenceNr, timestamp)));
        }

        /// <summary>
        /// Delete all snapshots matching <paramref name="criteria"/>.
        /// </summary>
        public void DeleteSnapshots(SnapshotSelectionCriteria criteria)
        {
            SnapshotStore.Tell(new DeleteSnapshots(SnapshotterId, criteria));
        }

        private IStash CreateStash()
        {
            return Context.CreateStash(GetType());
        }
    }
}

