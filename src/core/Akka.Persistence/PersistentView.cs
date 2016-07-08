//-----------------------------------------------------------------------
// <copyright file="PersistentView.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Newtonsoft.Json;

namespace Akka.Persistence
{
    /// <summary>
    /// Instructs a <see cref="PersistentView"/> to update itself. This will run a single incremental message replay 
    /// with all messages from the corresponding persistent id's journal that have not yet been consumed by the view.  
    /// To update a view with messages that have been written after handling this request, another <see cref="Update"/> 
    /// request must be sent to the view.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
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

#if SERIALIZATION
    [Serializable]
#endif
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
    public abstract partial class PersistentView : ActorBase, ISnapshotter, IPersistentIdentity, IWithUnboundedStash, IPersistenceRecovery
    {
        protected readonly PersistenceExtension Extension;
        private readonly ILoggingAdapter _log;

        private readonly PersistenceSettings.ViewSettings _viewSettings;
        private IActorRef _snapshotStore;
        private IActorRef _journal;

        private ICancelable _scheduleCancellation;

        private IStash _internalStash;
        private ViewState _currentState;

        protected PersistentView()
        {
            LastSequenceNr = 0L;
            Extension = Persistence.Instance.Apply(Context.System);
            _viewSettings = Extension.Settings.View;
            _internalStash = CreateStash();
            _currentState = RecoveryStarted(long.MaxValue);
            _log = Context.GetLogger();
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
        /// Returns true if this persistent view is currently recovering.
        /// </summary>
        public bool IsRecovering { get { return _currentState.IsRecoveryRunning; } }

        /// <summary>
        /// Returns true if this persistent view has successfully finished recovery.
        /// </summary>
        public bool IsRecoveryFinished { get { return !IsRecovering; } }

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

        private void UpdateLastSequenceNr(IPersistentRepresentation persistent)
        {
            if (persistent.SequenceNr > LastSequenceNr) LastSequenceNr = persistent.SequenceNr;
        }

        /// <summary>
        /// Called when the persistent view is started for the first time.
        /// The returned <see cref="Akka.Persistence.Recovery"/> object defines how the actor
        /// will recover its persistent state behore handling the first incoming message.
        /// 
        /// To skip recovery completely return <see cref="Akka.Persistence.Recovery.None"/>.
        /// </summary>
        public virtual Recovery Recovery { get { return new Recovery(replayMax: AutoUpdateReplayMax); } }

        /// <summary>
        /// Called whenever a message replay fails. By default it logs the error.
        /// Subclass may override to customize logging.
        /// The <see cref="PersistentView"/> will not stop or throw exception due to this.
        /// It will try again on next update.
        /// </summary>
        /// <returns></returns>
        protected virtual void OnReplayError(Exception cause)
        {
            if (_log.IsErrorEnabled)
                _log.Error(cause, "Persistence view failure when replaying events for persistenceId [{0}]. " +
                                  "Last known sequence number [{}]", PersistenceId, LastSequenceNr);
        }

        private void ChangeState(ViewState state)
        {
            _currentState = state;
        }

        /// <summary>
        /// Instructs the snapshot store to load the specified snapshot and send it via an
        /// <see cref="SnapshotOffer"/> to the running <see cref="PersistentActor"/>.
        /// </summary>
        public void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            SnapshotStore.Tell(new LoadSnapshot(persistenceId, criteria, toSequenceNr));
        }

        /// <summary>
        /// Saves <paramref name="snapshot"/> of current <see cref="ISnapshotter"/> state.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the success or failure of this
        /// via an <see cref="SaveSnapshotSuccess"/> or <see cref="SaveSnapshotFailure"/> message.
        /// </summary>
        public void SaveSnapshot(object snapshot)
        {
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(SnapshotterId, SnapshotSequenceNr), snapshot));
        }

        /// <summary>
        /// Deletes the snapshot identified by <paramref name="sequenceNr"/>.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the status of the deletion
        /// via an <see cref="DeleteSnapshotSuccess"/> or <see cref="DeleteSnapshotFailure"/> message.
        /// </summary>
        public void DeleteSnapshot(long sequenceNr)
        {
            SnapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(SnapshotterId, sequenceNr)));
        }

        /// <summary>
        /// Deletes all snapshots matching <paramref name="criteria"/>.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the status of the deletion
        /// via an <see cref="DeleteSnapshotsSuccess"/> or <see cref="DeleteSnapshotsFailure"/> message.
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

