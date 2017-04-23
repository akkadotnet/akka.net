//-----------------------------------------------------------------------
// <copyright file="Eventsourced.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Persistence
{
    /// <summary>
    /// INTERNAL API
    /// Implementation details of <see cref="UntypedPersistentActor"/> and <see cref="ReceivePersistentActor"/>.
    /// </summary>
    public abstract class Eventsourced : ActorBase, IPersistentIdentity, IPersistenceStash, IPersistenceRecovery
    {
        private static readonly AtomicCounter InstanceCounter = new AtomicCounter(1);

        private readonly int _instanceId;
        private readonly string _writerGuid;
        private readonly IStash _internalStash;
        private IActorRef _snapshotStore;
        private IActorRef _journal;
        private ICollection<IPersistentEnvelope> _journalBatch = new List<IPersistentEnvelope>();
        private bool _isWriteInProgress;
        private long _sequenceNr;
        private EventsourcedState _currentState;
        private LinkedList<IPersistentEnvelope> _eventBatch = new LinkedList<IPersistentEnvelope>();

        /// Used instead of iterating `pendingInvocations` in order to check if safe to revert to processing commands
        private long _pendingStashingPersistInvocations = 0L;

        /// Holds user-supplied callbacks for persist/persistAsync calls
        private LinkedList<IPendingHandlerInvocation> _pendingInvocations = new LinkedList<IPendingHandlerInvocation>();

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly PersistenceExtension Extension;
        private readonly ILoggingAdapter _log;
        private IStash _stash;

        /// <summary>
        /// TBD
        /// </summary>
        protected Eventsourced()
        {
            LastSequenceNr = 0L;
            _isWriteInProgress = false;
            _sequenceNr = 0L;

            Extension = Persistence.Instance.Apply(Context.System);
            _instanceId = InstanceCounter.GetAndIncrement();
            _writerGuid = Guid.NewGuid().ToString();
            _currentState = null;
            _internalStash = CreateStash();
            _log = Context.GetLogger();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual ILoggingAdapter Log { get { return _log; } }

        /// <summary>
        /// Id of the persistent entity for which messages should be replayed.
        /// </summary>
        public abstract string PersistenceId { get; }

        /// <summary>
        /// Called when the persistent actor is started for the first time.
        /// The returned <see cref="Akka.Persistence.Recovery"/> object defines how the actor
        /// will recover its persistent state behore handling the first incoming message.
        /// 
        /// To skip recovery completely return <see cref="Akka.Persistence.Recovery.None"/>.
        /// </summary>
        public virtual Recovery Recovery { get { return Recovery.Default;} }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual IStashOverflowStrategy InternalStashOverflowStrategy
        {
            get { return Extension.DefaultInternalStashOverflowStrategy; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash
        {
            get { return _stash; }
            set { _stash = new InternalStashAwareStash(value, _internalStash); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string JournalPluginId { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        public string SnapshotPluginId { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Journal
        {
            get { return _journal ?? (_journal = Extension.JournalFor(JournalPluginId)); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef SnapshotStore
        {
            get { return _snapshotStore ?? (_snapshotStore = Extension.SnapshotStoreFor(SnapshotPluginId)); }
        }

        /// <summary>
        /// Returns <see cref="PersistenceId"/>.
        /// </summary>
        public string SnapshotterId { get { return PersistenceId; } }

        /// <summary>
        /// Returns true if this persistent entity is currently recovering.
        /// </summary>
        public bool IsRecovering
        {
            get
            {
                // _currentState is null if this is called from constructor
                return _currentState?.IsRecoveryRunning ?? true;
            }
        }

        /// <summary>
        /// Returns true if this persistent entity has successfully finished recovery.
        /// </summary>
        public bool IsRecoveryFinished => !IsRecovering;

        /// <summary>
        /// Highest received sequence number so far or `0L` if this actor 
        /// hasn't replayed  or stored any persistent events yet.
        /// </summary>
        public long LastSequenceNr { get; private set; }

        /// <summary>
        /// Returns <see cref="LastSequenceNr"/>
        /// </summary>
        public long SnapshotSequenceNr { get { return LastSequenceNr; } }
        
        /// <summary>
        /// Instructs the snapshot store to load the specified snapshot and send it via an
        /// <see cref="SnapshotOffer"/> to the running <see cref="PersistentActor"/>.
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
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
        /// <param name="snapshot">TBD</param>
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
        /// <param name="sequenceNr">TBD</param>
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
        /// <param name="criteria">TBD</param>
        public void DeleteSnapshots(SnapshotSelectionCriteria criteria)
        {
            SnapshotStore.Tell(new DeleteSnapshots(SnapshotterId, criteria));
        }

        /// <summary> 
        /// Recovery handler that receives persistent events during recovery. If a state snapshot has been captured and saved, 
        /// this handler will receive a <see cref="SnapshotOffer"/> message followed by events that are younger than offer itself.
        /// 
        /// This handler must not have side-effects other than changing persistent actor state i.e. it
        /// should not perform actions that may fail, such as interacting with external services,
        /// for example.
        /// 
        /// If there is a problem with recovering the state of the actor from the journal, the error
        /// will be logged and the actor will be stopped.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected abstract bool ReceiveRecover(object message);

        /// <summary>
        /// Command handler. Typically validates commands against current state - possibly by communicating with other actors.
        /// On successful validation, one or more events are derived from command and persisted.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected abstract bool ReceiveCommand(object message);

        /// <summary> 
        /// Asynchronously persists an <paramref name="event"/>. On successful persistence, the <paramref name="handler"/>
        /// is called with the persisted event. This method guarantees that no new commands will be received by a persistent actor
        /// between a call to <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> and execution of it's handler. It also
        /// holds multiple persist calls per received command. Internally this is done by stashing. The stash used
        /// for that is an internal stash which doesn't interfere with the inherited user stash.
        /// 
        /// 
        /// An event <paramref name="handler"/> may close over eventsourced actor state and modify it. Sender of the persistent event
        /// is considered a sender of the corresponding command. That means one can respond to sender from within an event handler.
        /// 
        /// 
        /// Within an event handler, applications usually update persistent actor state using 
        /// persisted event data, notify listeners and reply to command senders.
        /// 
        ///
        /// If persistence of an event fails, <see cref="OnPersistFailure" /> will be invoked and the actor will
        /// unconditionally be stopped. The reason that it cannot resume when persist fails is that it
        /// is unknown if the event was actually persisted or not, and therefore it is in an inconsistent
        /// state. Restarting on persistent failures will most likely fail anyway, since the journal
        /// is probably unavailable. It is better to stop the actor and after a back-off timeout start
        /// it again.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="event">TBD</param>
        /// <param name="handler">TBD</param>
        public void Persist<TEvent>(TEvent @event, Action<TEvent> handler)
        {
            _pendingStashingPersistInvocations++;
            _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddFirst(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender)));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order.
        /// This is equivalent of multiple calls of <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> calls
        /// with the same handler, except that events are persisted atomically with this method.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            if (events == null) return;

            Action<object> inv = o => handler((TEvent)o);
            var persistents = ImmutableList<IPersistentRepresentation>.Empty.ToBuilder();
            foreach (var @event in events)
            {
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, inv));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }
            if (persistents.Count > 0)
                _eventBatch.AddFirst(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary> 
        /// Asynchronously persists an <paramref name="event"/>. On successful persistence, the <paramref name="handler"/>
        /// is called with the persisted event. Unlike <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> method,
        /// this one will continue to receive incoming commands between calls and executing it's event <paramref name="handler"/>.
        /// 
        /// 
        /// This version should be used in favor of <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> 
        /// method when throughput is more important that commands execution precedence.
        /// 
        /// 
        /// An event <paramref name="handler"/> may close over eventsourced actor state and modify it. Sender of the persistent event
        /// is considered a sender of the corresponding command. That means, one can respond to sender from within an event handler.
        /// 
        /// 
        /// Within an event handler, applications usually update persistent actor state using 
        /// persisted event data, notify listeners and reply to command senders.
        /// 
        /// 
        /// If persistence of an event fails, <see cref="OnPersistFailure" /> will be invoked and the actor will
        /// unconditionally be stopped. The reason that it cannot resume when persist fails is that it
        /// is unknown if the event was actually persisted or not, and therefore it is in an inconsistent
        /// state. Restarting on persistent failures will most likely fail anyway, since the journal
        /// is probably unavailable. It is better to stop the actor and after a back-off timeout start
        /// it again.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="event">TBD</param>
        /// <param name="handler">TBD</param>
        public void PersistAsync<TEvent>(TEvent @event, Action<TEvent> handler)
        {
            _pendingInvocations.AddLast(new AsyncHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddFirst(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender)));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order.
        /// This is equivalent of multiple calls of <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> calls
        /// with the same handler, except that events are persisted atomically with this method.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            Action<object> inv = o => handler((TEvent)o);
            foreach (var @event in events)
            {
                _pendingInvocations.AddLast(new AsyncHandlerInvocation(@event, inv));
            }
            _eventBatch.AddFirst(new AtomicWrite(events.Select(e => new Persistent(e, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Defer the <paramref name="handler"/> execution until all pending handlers have been executed. 
        /// Allows to define logic within the actor, which will respect the invocation-order-guarantee
        /// in respect to <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> calls.
        /// That is, if <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> was invoked before
        /// <see cref="DeferAsync{TEvent}"/>, the corresponding handlers will be
        /// invoked in the same order as they were registered in.
        /// 
        /// This call will NOT result in <paramref name="evt"/> being persisted, use
        /// <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> or
        /// <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> instead if the given
        /// <paramref name="evt"/> should be possible to replay.
        /// 
        /// If there are no pending persist handler calls, the <paramref name="handler"/> will be called immediately.
        /// 
        /// If persistence of an earlier event fails, the persistent actor will stop, and the
        /// <paramref name="handler"/> will not be run.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="evt">TBD</param>
        /// <param name="handler">TBD</param>
        public void DeferAsync<TEvent>(TEvent evt, Action<TEvent> handler)
        {
            if (_pendingInvocations.Count == 0)
            {
                handler(evt);
            }
            else
            {
                _pendingInvocations.AddLast(new AsyncHandlerInvocation(evt, o => handler((TEvent)o)));
                _eventBatch.AddFirst(new NonPersistentMessage(evt, Sender));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="toSequenceNr">TBD</param>
        public void DeleteMessages(long toSequenceNr)
        {
            Journal.Tell(new DeleteMessagesTo(PersistenceId, toSequenceNr, Self));
        }

        /// <summary>
        /// Called whenever a message replay succeeds.
        /// </summary>
        protected virtual void OnReplaySuccess() { }

        /// <summary>
        /// Called whenever a message replay fails. By default it log the errors.
        /// </summary>
        /// <param name="reason">Reason of failure</param>
        /// <param name="message">Message that caused a failure</param>
        protected virtual void OnRecoveryFailure(Exception reason, object message = null)
        {
            if (message != null)
            {
               _log.Error(reason, "Exception in ReceiveRecover when replaying event type [{0}] with sequence number [{1}] for persistenceId [{2}]", 
                   message.GetType(), LastSequenceNr, PersistenceId);
            }
            else
            {
                _log.Error(reason, "Persistence failure when replaying events for persistenceId [{0}]. Last known sequence number [{1}]", PersistenceId, LastSequenceNr);
            }
        }

        /// <summary>
        /// Called when persist fails. By default it logs the error.
        /// Subclass may override to customize logging and for example send negative
        /// acknowledgment to sender.
        ///
        /// The actor is always stopped after this method has been invoked.
        ///
        /// Note that the event may or may not have been saved, depending on the type of
        /// failure.
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="event">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        protected virtual void OnPersistFailure(Exception cause, object @event, long sequenceNr)
        {
            _log.Error(cause, "Failed to persist event type [{0}] with sequence number [{1}] for persistenceId [{2}].",
                @event.GetType(), sequenceNr, PersistenceId);
        }

        /// <summary>
        /// Called when the journal rejected <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/> of an event.
        /// The event was not stored. By default this method logs the problem as a warning, and the actor continues.
        /// The callback handler that was passed to the <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/>
        /// method will not be invoked.
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="event">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        protected virtual void OnPersistRejected(Exception cause, object @event, long sequenceNr)
        {
            if (_log.IsWarningEnabled)
                _log.Warning("Rejected to persist event type [{0}] with sequence number [{1}] for persistenceId [{2}] due to [{3}].",
                    @event.GetType(), sequenceNr, PersistenceId, cause.Message);
        }

        private void ChangeState(EventsourcedState state)
        {
            _currentState = state;
        }

        private void UpdateLastSequenceNr(IPersistentRepresentation persistent)
        {
            if (persistent.SequenceNr > LastSequenceNr) LastSequenceNr = persistent.SequenceNr;
        }

        private long NextSequenceNr()
        {
            return (++_sequenceNr);
        }

        private void FlushJournalBatch()
        {
            if (!_isWriteInProgress && _journalBatch.Count > 0)
            {
                Journal.Tell(new WriteMessages(_journalBatch.ToArray(), Self, _instanceId));
                _journalBatch = new List<IPersistentEnvelope>(0);
                _isWriteInProgress = true;
            }
        }

        private IStash CreateStash()
        {
            return Context.CreateStash(GetType());
        }

        private void StashInternally(object currentMessage)
        {
            try
            {
                _internalStash.Stash();
            }
            catch(StashOverflowException e)
            {
                var strategy = InternalStashOverflowStrategy;
                if (strategy is DiscardToDeadLetterStrategy)
                {
                    var sender = Sender;
                    Context.System.DeadLetters.Tell(new DeadLetter(currentMessage, sender, Self), Sender);
                }
                else if (strategy is ReplyToStrategy)
                {
                    Sender.Tell(((ReplyToStrategy)strategy).Response);
                }
                else if (strategy is ThrowOverflowExceptionStrategy)
                {
                    throw;
                }
                else // should not happen
                {
                    throw;
                }
            }
        }

        private void UnstashInternally(bool all)
        {
            if (all)
                _internalStash.UnstashAll();
            else
                _internalStash.Unstash();
        }

        private class InternalStashAwareStash : IStash
        {
            private readonly IStash _userStash;
            private readonly IStash _internalStash;

            public InternalStashAwareStash(IStash userStash, IStash internalStash)
            {
                _userStash = userStash;
                _internalStash = internalStash;
            }

            public void Stash()
            {
                _userStash.Stash();
            }

            public void Unstash()
            {
                _userStash.Unstash();
            }

            public void UnstashAll()
            {
                // Internally, all messages are processed by unstashing them from
                // the internal stash one-by-one. Hence, an unstashAll() from the
                // user stash must be prepended to the internal stash.

                _internalStash.Prepend(ClearStash());
            }

            public void UnstashAll(Func<Envelope, bool> predicate)
            {
                _userStash.UnstashAll(predicate);
            }

            public IEnumerable<Envelope> ClearStash()
            {
                return _userStash.ClearStash();
            }

            public void Prepend(IEnumerable<Envelope> envelopes)
            {
                _userStash.Prepend(envelopes);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Func<Envelope, bool> UnstashFilterPredicate =
            envelope => !(envelope.Message is WriteMessageSuccess || envelope.Message is ReplayedMessage);

        private void StartRecovery(Recovery recovery)
        {
            ChangeState(RecoveryStarted(recovery.ReplayMax));
            LoadSnapshot(SnapshotterId, recovery.FromSnapshot, recovery.ToSequenceNr);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            _currentState.StateReceive(receive, message);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void AroundPreStart()
        {
            // Fail fast on missing plugins.
            var j = Journal;
            var s = SnapshotStore;
            StartRecovery(Recovery);
            base.AroundPreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="message">TBD</param>
        public override void AroundPreRestart(Exception cause, object message)
        {
            try
            {
                _internalStash.UnstashAll();
                Stash.UnstashAll(UnstashFilterPredicate);
            }
            finally
            {
                object inner;
                if (message is WriteMessageSuccess) inner = (message as WriteMessageSuccess).Persistent;
                else if (message is LoopMessageSuccess) inner = (message as LoopMessageSuccess).Message;
                else if (message is ReplayedMessage) inner = (message as ReplayedMessage).Persistent;
                else inner = null;

                FlushJournalBatch();
                base.AroundPreRestart(cause, inner);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
        public override void AroundPostRestart(Exception reason, object message)
        {
            StartRecovery(Recovery);
            base.AroundPostRestart(reason, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void AroundPostStop()
        {
            try
            {
                _internalStash.UnstashAll();
                Stash.UnstashAll(UnstashFilterPredicate);
            }
            finally
            {
                base.AroundPostStop();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void Unhandled(object message)
        {
            if (message is RecoveryCompleted) return; // ignore
            if (message is SaveSnapshotFailure)
            {
                var m = (SaveSnapshotFailure)message;
                if (_log.IsWarningEnabled)
                    _log.Warning("Failed to SaveSnapshot given metadata [{0}] due to: [{1}: {2}]", m.Metadata, m.Cause, m.Cause.Message);
            }
            if (message is DeleteSnapshotFailure)
            {
                var m = (DeleteSnapshotFailure)message;
                if (_log.IsWarningEnabled)
                    _log.Warning("Failed to DeleteSnapshot given metadata [{0}] due to: [{1}: {2}]", m.Metadata, m.Cause, m.Cause.Message);
            }
            if (message is DeleteSnapshotsFailure)
            {
                var m = (DeleteSnapshotsFailure)message;
                if (_log.IsWarningEnabled)
                    _log.Warning("Failed to DeleteSnapshots given criteria [{0}] due to: [{1}: {2}]", m.Criteria, m.Cause, m.Cause.Message);
            }
            if (message is DeleteMessagesFailure)
            {
                var m = (DeleteMessagesFailure)message;
                if (_log.IsWarningEnabled)
                    _log.Warning("Failed to DeleteMessages ToSequenceNr [{0}] for PersistenceId [{1}] due to: [{2}: {3}]", m.ToSequenceNr, PersistenceId, m.Cause, m.Cause.Message);
            }
            base.Unhandled(message);
        }

        /// <summary>
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered with a <see cref="SnapshotOffer"/> 
        /// message to the actor's <see cref="ReceiveRecover"/>. Then initiates a message replay, either starting 
        /// from the loaded snapshot or from scratch, and switches to <see cref="ReplayStarted"/> state. 
        /// All incoming messages are stashed.
        /// </summary>
        /// <param name="maxReplays">Maximum number of messages to replay</param>
        private EventsourcedState RecoveryStarted(long maxReplays)
        {
            // protect against snapshot stalling forever because journal overloaded and such
            var timeout = Extension.JournalConfigFor(JournalPluginId).GetTimeSpan("recovery-event-timeout", null, false);
            var timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, new RecoveryTick(true), Self);

            Receive recoveryBehavior = message =>
            {
                Receive receiveRecover = ReceiveRecover;
                if (message is IPersistentRepresentation && IsRecovering)
                    return receiveRecover((message as IPersistentRepresentation).Payload);
                else if (message is SnapshotOffer)
                    return receiveRecover((SnapshotOffer)message);
                else if (message is RecoveryCompleted)
                    return receiveRecover(RecoveryCompleted.Instance);
                else return false;
            };

            return new EventsourcedState("recovery started - replay max: " + maxReplays, true, (receive, message) =>
            {
                if (message is LoadSnapshotResult)
                {
                    var res = (LoadSnapshotResult)message;
                    timeoutCancelable.Cancel();
                    if (res.Snapshot != null)
                    {
                        var snapshot = res.Snapshot;
                        LastSequenceNr = snapshot.Metadata.SequenceNr;
                        // Since we are recovering we can ignore the receive behavior from the stack
                        base.AroundReceive(recoveryBehavior, new SnapshotOffer(snapshot.Metadata, snapshot.Snapshot));
                    }

                    ChangeState(Recovering(recoveryBehavior, timeout));
                    Journal.Tell(new ReplayMessages(LastSequenceNr + 1L, res.ToSequenceNr, maxReplays, PersistenceId, Self));
                }
                else if (message is RecoveryTick)
                {
                    try
                    {
                        OnRecoveryFailure(
                            new RecoveryTimedOutException(
                                $"Recovery timed out, didn't get snapshot within {timeout.TotalSeconds}s."));
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
                else
                    StashInternally(message);
            });
        }

        /// <summary>
        /// Processes replayed messages, if any. The actor's <see cref="ReceiveRecover"/> is invoked with the replayed events.
        /// 
        /// If replay succeeds it got highest stored sequence number response from the journal and then switches
        /// to <see cref="ProcessingCommands"/> state.
        /// If replay succeeds the <see cref="OnReplaySuccess"/> callback method is called, otherwise
        /// <see cref="OnRecoveryFailure"/>.
        /// 
        /// All incoming messages are stashed.
        /// </summary>
        private EventsourcedState Recovering(Receive recoveryBehavior, TimeSpan timeout)
        {
            // protect against event replay stalling forever because of journal overloaded and such
            var timeoutCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(timeout, timeout, Self,
                new RecoveryTick(false), Self);
            var eventSeenInInterval = false;

            return new EventsourcedState("replay started", true, (receive, message) =>
            {
                if (message is ReplayedMessage)
                {
                    var m = (ReplayedMessage)message;
                    try
                    {
                        eventSeenInInterval = true;
                        UpdateLastSequenceNr(m.Persistent);
                        base.AroundReceive(recoveryBehavior, m.Persistent);
                    }
                    catch (Exception cause)
                    {
                        timeoutCancelable.Cancel();
                        try
                        {
                            OnRecoveryFailure(cause, m.Persistent.Payload);
                        }
                        finally
                        {
                            Context.Stop(Self);
                        }
                    }
                }
                else if (message is RecoverySuccess)
                {
                    var m = (RecoverySuccess)message;
                    timeoutCancelable.Cancel();
                    OnReplaySuccess();
                    ChangeState(ProcessingCommands());
                    _sequenceNr = m.HighestSequenceNr;
                    LastSequenceNr = m.HighestSequenceNr;
                    _internalStash.UnstashAll();
                    base.AroundReceive(recoveryBehavior, RecoveryCompleted.Instance);
                }
                else if (message is ReplayMessagesFailure)
                {
                    var failure = (ReplayMessagesFailure)message;
                    timeoutCancelable.Cancel();
                    try
                    {
                        OnRecoveryFailure(failure.Cause, message: null);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
                else if (message is RecoveryTick)
                {
                    var isSnapshotTick = ((RecoveryTick)message).Snapshot;
                    if (!isSnapshotTick)
                    {
                        if (!eventSeenInInterval)
                        {
                            timeoutCancelable.Cancel();
                            try
                            {
                                OnRecoveryFailure(
                                    new RecoveryTimedOutException(
                                        $"Recovery timed out, didn't get event within {timeout.TotalSeconds}s, highest sequence number seen {_sequenceNr}."));
                            }
                            finally
                            {
                                Context.Stop(Self);
                            }
                        }
                        else
                        {
                            eventSeenInInterval = false;
                        }
                    }
                    else
                    {
                        // snapshot tick, ignore
                    }
                }
                else
                    StashInternally(message);
            });
        }

        /// <summary>
        /// If event persistence is pending after processing a command, event persistence 
        /// is triggered and the state changes to <see cref="PersistingEvents"/>.
        /// </summary>
        private EventsourcedState ProcessingCommands()
        {
            return new EventsourcedState("processing commands", false, (receive, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, err =>
                {
                    _pendingInvocations.Pop();
                    UnstashInternally(err);
                });
                if (!handled)
                {
                    try
                    {
                        base.AroundReceive(receive, message);
                        OnProcessingCommandsAroundReceiveComplete(false);
                    }
                    catch (Exception)
                    {
                        OnProcessingCommandsAroundReceiveComplete(true);
                        throw;
                    }
                }
            });
        }

        private void OnProcessingCommandsAroundReceiveComplete(bool err)
        {
            if (_eventBatch.Count > 0) FlushBatch();

            if (_pendingStashingPersistInvocations > 0)
                ChangeState(PersistingEvents());
            else
                UnstashInternally(err);
        }

        private void FlushBatch()
        {
            if (_eventBatch.Count > 0)
            {
                foreach (var p in _eventBatch.Reverse())
                {
                    _journalBatch.Add(p);
                }
                _eventBatch = new LinkedList<IPersistentEnvelope>();
            }

            FlushJournalBatch();
        }

        /// <summary>
        /// Remains until pending events are persisted and then changes state to <see cref="ProcessingCommands"/>.
        /// Only events to be persisted are processed. All other messages are stashed internally.
        /// </summary>
        private EventsourcedState PersistingEvents()
        {
            return new EventsourcedState("persisting events", false, (receive, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, err =>
                {
                    var invocation = _pendingInvocations.Pop();

                    // enables an early return to `processingCommands`, because if this counter hits `0`,
                    // we know the remaining pendingInvocations are all `persistAsync` created, which
                    // means we can go back to processing commands also - and these callbacks will be called as soon as possible
                    if (invocation is StashingHandlerInvocation)
                        _pendingStashingPersistInvocations--;

                    if (_pendingStashingPersistInvocations == 0)
                    {
                        ChangeState(ProcessingCommands());
                        UnstashInternally(err);
                    }
                });

                if (!handled)
                    StashInternally(message);
            });
        }

        private void PeekApplyHandler(object payload)
        {
            try
            {
                _pendingInvocations.First.Value.Handler(payload);
            }
            finally
            {
                FlushBatch();
            }
        }

        private bool CommonProcessingStateBehavior(object message, Action<bool> onWriteMessageComplete)
        {
            // _instanceId mismatch can happen for persistAsync and defer in case of actor restart
            // while message is in flight, in that case we ignore the call to the handler
            if (message is WriteMessageSuccess)
            {
                var m = (WriteMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    UpdateLastSequenceNr(m.Persistent);
                    try
                    {
                        PeekApplyHandler(m.Persistent.Payload);
                        onWriteMessageComplete(false);
                    }
                    catch
                    {
                        onWriteMessageComplete(true);
                        throw;
                    }
                }
            }
            else if (message is WriteMessageRejected)
            {
                var m = (WriteMessageRejected)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    var p = m.Persistent;
                    UpdateLastSequenceNr(p);
                    onWriteMessageComplete(false);
                    OnPersistRejected(m.Cause, p.Payload, p.SequenceNr);
                }
            }
            else if (message is WriteMessageFailure)
            {
                var m = (WriteMessageFailure)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    var p = m.Persistent;
                    onWriteMessageComplete(false);
                    try
                    {
                        OnPersistFailure(m.Cause, p.Payload, p.SequenceNr);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
            }
            else if (message is LoopMessageSuccess)
            {
                var m = (LoopMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    try
                    {
                        PeekApplyHandler(m.Message);
                        onWriteMessageComplete(false);
                    }
                    catch (Exception)
                    {
                        onWriteMessageComplete(true);
                        throw;
                    }
                }
            }
            else if (message is WriteMessagesSuccessful)
            {
                _isWriteInProgress = false;
                FlushJournalBatch();
            }
            else if (message is WriteMessagesFailed)
            {
                _isWriteInProgress = false;
                // it will be stopped by the first WriteMessageFailure message
            }
            else if (message is RecoveryTick)
            {
                // we may have one of these in the mailbox before the scheduled timeout
                // is cancelled when recovery has completed, just consume it so the concrete actor never sees it
            }
            else return false;
            return true;
        }
    }
}
