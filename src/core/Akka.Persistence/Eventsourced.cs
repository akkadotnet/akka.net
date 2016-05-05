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
    public interface IPendingHandlerInvocation
    {
        object Event { get; }
        Action<object> Handler { get; }
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// </summary>
    public sealed class StashingHandlerInvocation : IPendingHandlerInvocation
    {
        public StashingHandlerInvocation(object evt, Action<object> handler)
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; private set; }
        public Action<object> Handler { get; private set; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Originates from <see cref="Eventsourced.PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> 
    /// or <see cref="Eventsourced.DeferAsync{TEvent}"/> method calls.
    /// </summary>
    public sealed class AsyncHandlerInvocation : IPendingHandlerInvocation
    {
        public AsyncHandlerInvocation(object evt, Action<object> handler)
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; private set; }
        public Action<object> Handler { get; private set; }
    }

    public abstract partial class Eventsourced : ActorBase, IPersistentIdentity, IPersistenceStash, IPersistenceRecovery
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

        protected readonly PersistenceExtension Extension;
        private readonly ILoggingAdapter _log;
        private IStash _stash;

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

        public virtual IStashOverflowStrategy InternalStashOverflowStrategy
        {
            get { return Extension.DefaultInternalStashOverflowStrategy; }
        }

        public IStash Stash
        {
            get { return _stash; }
            set { _stash = new InternalStashAwareStash(value, _internalStash); }
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
        /// Returns <see cref="PersistenceId"/>.
        /// </summary>
        public string SnapshotterId { get { return PersistenceId; } }

        /// <summary>
        /// Returns true if this persistent entity is currently recovering.
        /// </summary>
        public bool IsRecovering { get { return _currentState.IsRecoveryRunning; } }

        /// <summary>
        /// Returns true if this persistent entity has successfully finished recovery.
        /// </summary>
        public bool IsRecoveryFinished { get { return !IsRecovering; } }

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
        protected abstract bool ReceiveRecover(object message);

        /// <summary>
        /// Command handler. Typically validates commands against current state - possibly by communicating with other actors.
        /// On successful validation, one or more events are derived from command and persisted.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
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

        [Obsolete("Use PersistAll instead")]
        public void Persist<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            PersistAll(events, handler);
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

        [Obsolete("Use PersistAllAsync instead")]
        public void PersistAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            PersistAllAsync(events, handler);
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

        [Obsolete("Use DeferAsync instead")]
        public void Defer<TEvent>(TEvent evt, Action<TEvent> handler)
        {
            DeferAsync(evt, handler);
        }

        [Obsolete("Use DeferAsync instead")]
        public void Defer<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            foreach (var @event in events)
            {
                DeferAsync(@event, handler);
            }
        }

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
        protected virtual void OnPersistFailure(Exception cause, object @event, long sequenceNr)
        {
            _log.Error(cause, "Failed to persist event type [{0}] with sequence number [{1}] for persistenceId [{2}].",
                @event.GetType(), sequenceNr, PersistenceId);
        }

        /// <summary>
        /// Called when the journal rejected <see cref="PersistentActor.Persist{TEvent}(TEvent,Action{TEvent})"/> of an event.
        /// The event was not stored. By default this method logs the problem as a warning, and the actor continues.
        /// The callback handler that was passed to the <see cref="PersistentActor.Persist{TEvent}(TEvent,Action{TEvent})"/>
        /// method will not be invoked.
        /// </summary>
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
            if (!_isWriteInProgress)
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
    }
}

