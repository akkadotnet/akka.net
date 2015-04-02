using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
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
    /// or <see cref="Eventsourced.Defer{TEvent}(TEvent,System.Action{TEvent})"/> method calls.
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

    public abstract partial class Eventsourced : ActorBase, IPersistentIdentity, IWithUnboundedStash
    {
        private static readonly AtomicCounter InstanceCounter = new AtomicCounter(1);

        private readonly int _instanceId;
        private readonly IStash _internalStash;
        private IActorRef _snapshotStore;
        private IActorRef _journal;
        private ICollection<IPersistentEnvelope> _journalBatch = new List<IPersistentEnvelope>();
        private readonly int _maxMessageBatchSize;
        private bool _isWriteInProgress = false;
        private long _sequenceNr = 0L;
        private EventsourcedState _currentState;
        private LinkedList<IPersistentEnvelope> _eventBatch = new LinkedList<IPersistentEnvelope>();

        /// Used instead of iterating `pendingInvocations` in order to check if safe to revert to processing commands
        private long _pendingStashingPersistInvocations = 0L;

        /// Holds user-supplied callbacks for persist/persistAsync calls
        private LinkedList<IPendingHandlerInvocation> _pendingInvocations = new LinkedList<IPendingHandlerInvocation>();

        protected readonly PersistenceExtension Extension;

        protected Eventsourced()
        {
            LastSequenceNr = 0L;

            Extension = Persistence.Instance.Apply(Context.System);
            _instanceId = InstanceCounter.GetAndIncrement();
            _maxMessageBatchSize = Extension.Settings.Journal.MaxMessageBatchSize;
            _currentState = RecoveryPending();
            _internalStash = CreateStash();
        }
        
        /// <summary>
        /// Id of the persistent entity for which messages should be replayed.
        /// </summary>
        public abstract string PersistenceId { get; }

        public IStash Stash { get; set; }

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
        
        public void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            SnapshotStore.Tell(new LoadSnapshot(persistenceId, criteria, toSequenceNr));
        }

        public void SaveSnapshot(object snapshot)
        {
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(SnapshotterId, SnapshotSequenceNr), snapshot));
        }

        public void DeleteSnapshot(long sequenceNr, DateTime timestamp)
        {
            SnapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(SnapshotterId, sequenceNr, timestamp)));
        }

        public void DeleteSnapshots(SnapshotSelectionCriteria criteria)
        {
            SnapshotStore.Tell(new DeleteSnapshots(SnapshotterId, criteria));
        }

        /// <summary> 
        /// Recovery handler that receives persistent events during recovery. If a state snapshot has been captured and saved, 
        /// this handler will receive a <see cref="SnapshotOffer"/> message followed by events that are younger than offer itself.
        /// 
        /// This handler must be a pure function (no side effects allowed), it should not perform any actions that may fail. 
        /// If recovery fails this actor will be stopped. This can be customized in <see cref="RecoveryFailure"/>. 
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
        /// holds multiple persist calls per received command. Internally this is done by stashing.
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
        /// If persistence of an event fails, the persistent actor will be stopped. 
        /// This can be customized by handling <see cref="PersistenceFailure"/> in <see cref="ReceiveCommand"/> method. 
        /// </summary>
        public void Persist<TEvent>(TEvent @event, Action<TEvent> handler)
        {
            _pendingStashingPersistInvocations++;
            _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddFirst(new Persistent(@event));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order.
        /// This is equivalent of multiple calls of <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> calls.
        /// </summary>
        public void Persist<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            Action<object> inv = o => handler((TEvent)o);
            foreach (var @event in events)
            {
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, inv));
                _eventBatch.AddFirst(new Persistent(@event));
            }
        }

        /// <summary> 
        /// Asynchronously persists an <paramref name="event"/>. On successfull persistence, the <paramref name="handler"/>
        /// is called with the persisted event. Unlike <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> method,
        /// this one will continue to receive incomming commands between calls and executing it's event <paramref name="handler"/>.
        /// 
        /// 
        /// This version should be used in favour of <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> 
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
        /// If persistence of an event fails, the persistent actor will be stopped. 
        /// This can be customized by handling <see cref="PersistenceFailure"/> in <see cref="ReceiveCommand"/> method. 
        /// </summary>
        public void PersistAsync<TEvent>(TEvent @event, Action<TEvent> handler)
        {
            _pendingInvocations.AddLast(new AsyncHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddFirst(new Persistent(@event));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order.
        /// This is equivalent of multiple calls of <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> calls.
        /// </summary>
        public void PersistAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            Action<object> inv = o => handler((TEvent)o);
            foreach (var @event in events)
            {
                _pendingInvocations.AddLast(new AsyncHandlerInvocation(@event, inv));
                _eventBatch.AddFirst(new Persistent(@event));
            }
        }

        /// <summary>
        /// 
        /// Defer the <paramref name="handler"/> execution until all pending handlers have been executed. 
        /// If <see cref="PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> was invoked before defer, 
        /// the corresponding handlers will be invoked in the same order as they were registered in.
        /// 
        /// 
        /// This call will NOT result in persisted event. If it should be possible to replay use persist method instead.
        /// If there are not awaiting persist handler calls, the <paramref name="handler"/> will be invoced immediately.
        /// 
        /// </summary>
        public void Defer<TEvent>(TEvent evt, Action<TEvent> handler)
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

        public void Defer<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            foreach (var @event in events)
            {
                Defer(@event, handler);
            }
        }

        public void DeleteMessages(long toSequenceNr, bool permanent)
        {
            Journal.Tell(new DeleteMessagesTo(PersistenceId, toSequenceNr, permanent));
        }

        public void UnstashAll()
        {
            // Internally, all messages are processed by unstashing them from
            // the internal stash one-by-one. Hence, an unstashAll() from the
            // user stash must be prepended to the internal stash.

            _internalStash.Prepend(Stash.ClearStash());
        }

        /// <summary>
        /// Called whenever a message replay succeeds.
        /// </summary>
        protected virtual void OnReplaySuccess() { }

        /// <summary>
        /// Called whenever a message replay fails.
        /// </summary>
        /// <param name="reason">Reason of failure</param>
        protected virtual void OnReplayFailure(Exception reason) { }

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
            Journal.Tell(new WriteMessages(_journalBatch.ToArray(), Self, _instanceId));
            _journalBatch = new List<IPersistentEnvelope>(0);
            _isWriteInProgress = true;
        }

        private IStash CreateStash()
        {
            return Context.CreateStash(GetType());
        }

        private static bool CanUnstashFilterPredicate(object message)
        {
            return !(message is WriteMessageSuccess || message is ReplayedMessage);
        }
    }
}