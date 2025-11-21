//-----------------------------------------------------------------------
// <copyright file="Eventsourced.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;
using System.Threading.Tasks;

namespace Akka.Persistence
{
    internal interface IPendingHandlerInvocation
    {
        object Event { get; }
        bool IsLastInBatch { get; }
    }

    internal interface ISyncHandlerInvocation : IPendingHandlerInvocation
    {
        Action<object> Handler { get; }
    }

    internal interface IAsyncHandlerInvocation : IPendingHandlerInvocation
    {
        Func<object, Task> AsyncHandler { get; }
    }

    internal interface ISyncCompletionInvocation : IPendingHandlerInvocation
    {
        Action CompletionCallback { get; }
    }

    internal interface IAsyncCompletionInvocation : IPendingHandlerInvocation
    {
        Func<Task> AsyncCompletionCallback { get; }
    }

    /// <summary>
    /// Marker interface for stashing invocations that require command stashing.
    /// </summary>
    internal interface IStashingInvocation : IPendingHandlerInvocation
    {
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// Sync handler with optional sync completion.
    /// </summary>
    internal sealed class StashingHandlerInvocation : ISyncHandlerInvocation, ISyncCompletionInvocation, IStashingInvocation
    {
        public StashingHandlerInvocation(object evt, Action<object> handler, bool isLastInBatch = false,
            Action completionCallback = null)
        {
            Event = evt;
            Handler = handler;
            IsLastInBatch = isLastInBatch;
            CompletionCallback = completionCallback;
        }

        public object Event { get; }
        public Action<object> Handler { get; }
        public bool IsLastInBatch { get; }
        public Action CompletionCallback { get; }
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// Sync handler with optional async completion.
    /// </summary>
    internal sealed class StashingHandlerInvocationWithAsyncCompletion : ISyncHandlerInvocation, IAsyncCompletionInvocation, IStashingInvocation
    {
        public StashingHandlerInvocationWithAsyncCompletion(object evt, Action<object> handler, bool isLastInBatch = false,
            Func<Task> asyncCompletionCallback = null)
        {
            Event = evt;
            Handler = handler;
            IsLastInBatch = isLastInBatch;
            AsyncCompletionCallback = asyncCompletionCallback;
        }

        public object Event { get; }
        public Action<object> Handler { get; }
        public bool IsLastInBatch { get; }
        public Func<Task> AsyncCompletionCallback { get; }
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// Async handler with optional sync completion.
    /// </summary>
    internal sealed class StashingAsyncHandlerInvocation : IAsyncHandlerInvocation, ISyncCompletionInvocation, IStashingInvocation
    {
        public StashingAsyncHandlerInvocation(object evt, Func<object, Task> asyncHandler, bool isLastInBatch = false,
            Action completionCallback = null)
        {
            Event = evt;
            AsyncHandler = asyncHandler;
            IsLastInBatch = isLastInBatch;
            CompletionCallback = completionCallback;
        }

        public object Event { get; }
        public Func<object, Task> AsyncHandler { get; }
        public bool IsLastInBatch { get; }
        public Action CompletionCallback { get; }
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// Async handler with optional async completion.
    /// </summary>
    internal sealed class StashingAsyncHandlerInvocationWithAsyncCompletion : IAsyncHandlerInvocation, IAsyncCompletionInvocation, IStashingInvocation
    {
        public StashingAsyncHandlerInvocationWithAsyncCompletion(object evt, Func<object, Task> asyncHandler, bool isLastInBatch = false,
            Func<Task> asyncCompletionCallback = null)
        {
            Event = evt;
            AsyncHandler = asyncHandler;
            IsLastInBatch = isLastInBatch;
            AsyncCompletionCallback = asyncCompletionCallback;
        }

        public object Event { get; }
        public Func<object, Task> AsyncHandler { get; }
        public bool IsLastInBatch { get; }
        public Func<Task> AsyncCompletionCallback { get; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Originates from <see cref="Eventsourced.PersistAsync{TEvent}(TEvent,Action{TEvent})"/>
    /// or <see cref="Eventsourced.DeferAsync{TEvent}"/> method calls.
    /// Sync handler with optional sync completion.
    /// </summary>
    internal sealed class NonStashingHandlerInvocation : ISyncHandlerInvocation, ISyncCompletionInvocation
    {
        public NonStashingHandlerInvocation(object evt, Action<object> handler, bool isLastInBatch = false,
            Action completionCallback = null)
        {
            Event = evt;
            Handler = handler;
            IsLastInBatch = isLastInBatch;
            CompletionCallback = completionCallback;
        }

        public object Event { get; }
        public Action<object> Handler { get; }
        public bool IsLastInBatch { get; }
        public Action CompletionCallback { get; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Sync handler with optional async completion.
    /// </summary>
    internal sealed class NonStashingHandlerInvocationWithAsyncCompletion : ISyncHandlerInvocation, IAsyncCompletionInvocation
    {
        public NonStashingHandlerInvocationWithAsyncCompletion(object evt, Action<object> handler, bool isLastInBatch = false,
            Func<Task> asyncCompletionCallback = null)
        {
            Event = evt;
            Handler = handler;
            IsLastInBatch = isLastInBatch;
            AsyncCompletionCallback = asyncCompletionCallback;
        }

        public object Event { get; }
        public Action<object> Handler { get; }
        public bool IsLastInBatch { get; }
        public Func<Task> AsyncCompletionCallback { get; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Async handler with optional sync completion.
    /// </summary>
    internal sealed class NonStashingAsyncHandlerInvocation : IAsyncHandlerInvocation, ISyncCompletionInvocation
    {
        public NonStashingAsyncHandlerInvocation(object evt, Func<object, Task> asyncHandler, bool isLastInBatch = false,
            Action completionCallback = null)
        {
            Event = evt;
            AsyncHandler = asyncHandler;
            IsLastInBatch = isLastInBatch;
            CompletionCallback = completionCallback;
        }

        public object Event { get; }
        public Func<object, Task> AsyncHandler { get; }
        public bool IsLastInBatch { get; }
        public Action CompletionCallback { get; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Async handler with optional async completion.
    /// </summary>
    internal sealed class NonStashingAsyncHandlerInvocationWithAsyncCompletion : IAsyncHandlerInvocation, IAsyncCompletionInvocation
    {
        public NonStashingAsyncHandlerInvocationWithAsyncCompletion(object evt, Func<object, Task> asyncHandler, bool isLastInBatch = false,
            Func<Task> asyncCompletionCallback = null)
        {
            Event = evt;
            AsyncHandler = asyncHandler;
            IsLastInBatch = isLastInBatch;
            AsyncCompletionCallback = asyncCompletionCallback;
        }

        public object Event { get; }
        public Func<object, Task> AsyncHandler { get; }
        public bool IsLastInBatch { get; }
        public Func<Task> AsyncCompletionCallback { get; }
    }

    /// <summary>
    /// Message used to detect that recovery timed out.
    /// </summary>
    public sealed class RecoveryTick
    {
        public RecoveryTick(bool snapshot)
        {
            Snapshot = snapshot;
        }

        public bool Snapshot { get; }
    }

    /// <summary>
    /// The base class for all persistent actors.
    /// </summary>
    public abstract partial class Eventsourced : ActorBase, IPersistentIdentity, IPersistenceStash, IPersistenceRecovery
    {
        private static readonly AtomicCounter InstanceCounter = new(1);

        private readonly int _instanceId;
        private readonly string _writerGuid;
        private readonly IStash _internalStash;
        private IActorRef _snapshotStore;
        private IActorRef _journal;
        private IActorRef _recoveryPermitter;
        private List<IPersistentEnvelope> _journalBatch = new();
        private bool _isWriteInProgress;
        private long _sequenceNr;
        private EventsourcedState _currentState;
        private LinkedList<IPersistentEnvelope> _eventBatch = new();
        private bool _asyncTaskRunning = false;

        /// Used instead of iterating `pendingInvocations` in order to check if safe to revert to processing commands
        private long _pendingStashingPersistInvocations = 0L;

        /// Holds user-supplied callbacks for persist/persistAsync calls
        private readonly LinkedList<IPendingHandlerInvocation> _pendingInvocations = new();

        /// <summary>
        /// TBD
        /// </summary>
        protected PersistenceExtension Extension { get; }

        private IStash _stash;

        /// <summary>
        /// Initializes a new instance of the <see cref="Eventsourced"/> class.
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
            Log = Context.GetLogger();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual ILoggingAdapter Log { get; }

        /// <summary>
        /// Id of the persistent entity for which messages should be replayed.
        /// </summary>
        public abstract string PersistenceId { get; }

        /// <summary>
        /// Called when the persistent actor is started for the first time.
        /// The returned <see cref="Akka.Persistence.Recovery"/> object defines how the actor
        /// will recover its persistent state before handling the first incoming message.
        ///
        /// To skip recovery completely return <see cref="Akka.Persistence.Recovery.None"/>.
        /// </summary>
        public virtual Recovery Recovery => Recovery.Default;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual IStashOverflowStrategy InternalStashOverflowStrategy => Extension.DefaultInternalStashOverflowStrategy;

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
        public IActorRef Journal => _journal ??= Extension.JournalFor(JournalPluginId);

        internal IActorRef RecoveryPermitter => _recoveryPermitter ??= Extension.RecoveryPermitterFor(JournalPluginId);
        
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef SnapshotStore => _snapshotStore ??= Extension.SnapshotStoreFor(SnapshotPluginId);

        /// <summary>
        /// Returns <see cref="PersistenceId"/>.
        /// </summary>
        public string SnapshotterId => PersistenceId;

        /// <summary>
        /// Returns true if this persistent entity is currently recovering.
        /// </summary>
        public bool IsRecovering => _currentState?.IsRecoveryRunning() ?? true;

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
        public long SnapshotSequenceNr => LastSequenceNr;

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
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(SnapshotterId, SnapshotSequenceNr, Context.System.Scheduler.Now.UtcDateTime), snapshot));
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
            SnapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(SnapshotterId, sequenceNr, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc))));
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
        /// between a call to <see cref="Persist{TEvent}(TEvent,System.Action{TEvent})"/> and execution of its handler. It also
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
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            _pendingStashingPersistInvocations++;
            _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddLast(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender)));
        }

        /// <summary>
        /// Asynchronously persists an <paramref name="event"/> with an async handler. On successful persistence, the <paramref name="handler"/>
        /// is called with the persisted event. This method guarantees that no new commands will be received by a persistent actor
        /// between a call to <see cref="Persist{TEvent}(TEvent,Func{TEvent,Task})"/> and execution of its handler.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="event">TBD</param>
        /// <param name="handler">Async handler invoked for the persisted event</param>
        public void Persist<TEvent>(TEvent @event, Func<TEvent, Task> handler)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            _pendingStashingPersistInvocations++;
            _pendingInvocations.AddLast(new StashingAsyncHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddLast(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
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
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null) return;

            void Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            foreach (var @event in events)
            {
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, Inv));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with a completion callback.
        /// The <paramref name="onComplete"/> callback is invoked after all event handlers have been executed.
        /// This method stashes incoming commands until all events are persisted and handlers executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="onComplete">Callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler, Action onComplete)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null)
            {
                onComplete?.Invoke();
                return;
            }

            void Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            var eventsList = events.ToList();
            var eventCount = eventsList.Count;

            if (eventCount == 0)
            {
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var @event = eventsList[i];
                var isLast = i == eventCount - 1;
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingHandlerInvocation(@event, Inv, isLast, isLast ? onComplete : null));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with an async completion callback.
        /// The <paramref name="onCompleteAsync"/> callback is invoked after all event handlers have been executed.
        /// This method stashes incoming commands until all events are persisted and handlers executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="onCompleteAsync">Async callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler, Func<Task> onCompleteAsync)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            void Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            var eventsList = events.ToList();
            var eventCount = eventsList.Count;

            if (eventCount == 0)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var @event = eventsList[i];
                var isLast = i == eventCount - 1;
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingHandlerInvocationWithAsyncCompletion(@event, Inv, isLast, isLast ? onCompleteAsync : null));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers.
        /// The <paramref name="handler"/> is an async function that will be executed for each event.
        /// This method stashes incoming commands until all events are persisted and handlers executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null) return;

            Task Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            foreach (var @event in events)
            {
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingAsyncHandlerInvocation(@event, Inv));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers and a completion callback.
        /// The <paramref name="onComplete"/> callback is invoked after all event handlers have been executed.
        /// This method stashes incoming commands until all events are persisted and handlers executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        /// <param name="onComplete">Callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler, Action onComplete)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null)
            {
                onComplete?.Invoke();
                return;
            }

            Task Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            var eventsList = events.ToList();
            var eventCount = eventsList.Count;

            if (eventCount == 0)
            {
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var @event = eventsList[i];
                var isLast = i == eventCount - 1;
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingAsyncHandlerInvocation(@event, Inv, isLast, isLast ? onComplete : null));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers and an async completion callback.
        /// The <paramref name="onCompleteAsync"/> callback is invoked after all event handlers have been executed.
        /// This method stashes incoming commands until all events are persisted and handlers executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        /// <param name="onCompleteAsync">Async callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAll<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler, Func<Task> onCompleteAsync)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (events == null)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            Task Inv(object o) => handler((TEvent)o);
            var persistents = ImmutableList.CreateBuilder<IPersistentRepresentation>();
            var eventsList = events.ToList();
            var eventCount = eventsList.Count;

            if (eventCount == 0)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var @event = eventsList[i];
                var isLast = i == eventCount - 1;
                _pendingStashingPersistInvocations++;
                _pendingInvocations.AddLast(new StashingAsyncHandlerInvocationWithAsyncCompletion(@event, Inv, isLast, isLast ? onCompleteAsync : null));
                persistents.Add(new Persistent(@event, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender));
            }

            if (persistents.Count > 0)
                _eventBatch.AddLast(new AtomicWrite(persistents.ToImmutable()));
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
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            _pendingInvocations.AddLast(new NonStashingHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddLast(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender)));
        }

        /// <summary>
        /// Asynchronously persists an <paramref name="event"/> with an async handler. On successful persistence, the <paramref name="handler"/>
        /// is called with the persisted event. Unlike <see cref="Persist{TEvent}(TEvent,Func{TEvent,Task})"/> method,
        /// this one will continue to receive incoming commands between calls and executing it's event <paramref name="handler"/>.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="event">TBD</param>
        /// <param name="handler">Async handler invoked for the persisted event</param>
        public void PersistAsync<TEvent>(TEvent @event, Func<TEvent, Task> handler)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            _pendingInvocations.AddLast(new NonStashingAsyncHandlerInvocation(@event, o => handler((TEvent)o)));
            _eventBatch.AddLast(new AtomicWrite(new Persistent(@event, persistenceId: PersistenceId,
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
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            void Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            foreach (var @event in enumerable)
            {
                _pendingInvocations.AddLast(new NonStashingHandlerInvocation(@event, Inv));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with a completion callback.
        /// The <paramref name="onComplete"/> callback is invoked after all event handlers have been executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="onComplete">Callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler, Action onComplete)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            void Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            var eventCount = enumerable.Length;

            if (eventCount == 0)
            {
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var isLast = i == eventCount - 1;
                _pendingInvocations.AddLast(new NonStashingHandlerInvocation(enumerable[i], Inv, isLast, isLast ? onComplete : null));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with an async completion callback.
        /// The <paramref name="onCompleteAsync"/> callback is invoked after all event handlers have been executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">TBD</param>
        /// <param name="onCompleteAsync">Async callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler, Func<Task> onCompleteAsync)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            void Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            var eventCount = enumerable.Length;

            if (eventCount == 0)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var isLast = i == eventCount - 1;
                _pendingInvocations.AddLast(new NonStashingHandlerInvocationWithAsyncCompletion(enumerable[i], Inv, isLast, isLast ? onCompleteAsync : null));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers.
        /// The <paramref name="handler"/> is an async function that will be executed for each event.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            Task Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            foreach (var @event in enumerable)
            {
                _pendingInvocations.AddLast(new NonStashingAsyncHandlerInvocation(@event, Inv));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers and a completion callback.
        /// The <paramref name="onComplete"/> callback is invoked after all event handlers have been executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        /// <param name="onComplete">Callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler, Action onComplete)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            Task Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            var eventCount = enumerable.Length;

            if (eventCount == 0)
            {
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var isLast = i == eventCount - 1;
                _pendingInvocations.AddLast(new NonStashingAsyncHandlerInvocation(enumerable[i], Inv, isLast, isLast ? onComplete : null));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
                    sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: Sender))
                .ToImmutableList<IPersistentRepresentation>()));
        }

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order with async event handlers and an async completion callback.
        /// The <paramref name="onCompleteAsync"/> callback is invoked after all event handlers have been executed.
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="events">TBD</param>
        /// <param name="handler">Async handler invoked for each persisted event</param>
        /// <param name="onCompleteAsync">Async callback invoked after all events are persisted and their handlers executed</param>
        public void PersistAllAsync<TEvent>(IEnumerable<TEvent> events, Func<TEvent, Task> handler, Func<Task> onCompleteAsync)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            Task Inv(object o) => handler((TEvent)o);
            var enumerable = events as TEvent[] ?? events.ToArray();
            var eventCount = enumerable.Length;

            if (eventCount == 0)
            {
                if (onCompleteAsync != null)
                    RunTask(onCompleteAsync);
                return;
            }

            for (int i = 0; i < eventCount; i++)
            {
                var isLast = i == eventCount - 1;
                _pendingInvocations.AddLast(new NonStashingAsyncHandlerInvocationWithAsyncCompletion(enumerable[i], Inv, isLast, isLast ? onCompleteAsync : null));
            }

            _eventBatch.AddLast(new AtomicWrite(enumerable.Select(e => new Persistent(e, persistenceId: PersistenceId,
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
            if (IsRecovering)
            {
                throw new InvalidOperationException("Cannot persist during replay. Events can be persisted when receiving RecoveryCompleted or later.");
            }

            if (_pendingInvocations.Count == 0)
            {
                handler(evt);
            }
            else
            {
                _pendingInvocations.AddLast(new NonStashingHandlerInvocation(evt, o => handler((TEvent)o)));
                _eventBatch.AddLast(new NonPersistentMessage(evt, Sender));
            }
        }

        /// <summary>
        /// Permanently deletes all persistent messages with sequence numbers less than or equal <paramref name="toSequenceNr"/>.
        /// If the delete is successful a <see cref="DeleteMessagesSuccess"/> will be sent to the actor.
        /// If the delete fails a <see cref="DeleteMessagesFailure"/> will be sent to the actor.
        ///
        /// The given <paramref name="toSequenceNr"/> must be less than or equal to <see cref="Eventsourced.LastSequenceNr"/>, otherwise
        /// <see cref="DeleteMessagesFailure"/> is sent to the actor without performing the delete. All persistent
        /// messages may be deleted without specifying the actual sequence number by using <see cref="long.MaxValue"/>
        /// as the <paramref name="toSequenceNr"/>.
        /// </summary>
        /// <param name="toSequenceNr">Upper sequence number bound of persistent messages to be deleted.</param>
        public void DeleteMessages(long toSequenceNr)
        {
            if (toSequenceNr == long.MaxValue || toSequenceNr <= LastSequenceNr)
                Journal.Tell(new DeleteMessagesTo(PersistenceId, toSequenceNr == long.MaxValue ? LastSequenceNr : toSequenceNr, Self));
            else
                Self.Tell(new DeleteMessagesFailure(new InvalidOperationException($"toSequenceNr [{toSequenceNr}] must be less than or equal to LastSequenceNr [{LastSequenceNr}]"), toSequenceNr));
        }

        /// <summary>
        /// An <see cref="Eventsourced"/> actor can request cleanup by deleting either a range of, or all persistent events.
        /// For example, on successful snapshot completion, delete messages within a configurable <paramref name="snapshotAfter"/>
        /// range that are less than or equal to the given <see cref="SnapshotMetadata.SequenceNr"/>
        /// (provided the <see cref="SnapshotMetadata.SequenceNr"/> is &lt;= to <see cref="Eventsourced.LastSequenceNr"/>).
        ///
        /// Or delete all by using `long.MaxValue` as the `toSequenceNr`
        /// {{{ m.copy(sequenceNr = long.MaxValue) }}}
        /// </summary>
        /// <param name="e"></param>
        /// <param name="keepNrOfBatches"></param>
        /// <param name="snapshotAfter"></param>
        internal void InternalDeleteMessagesBeforeSnapshot(SaveSnapshotSuccess e, int keepNrOfBatches, int snapshotAfter)
        {
            // Delete old events but keep the latest around
            // 1. It's not safe to delete all events immediately because snapshots are typically stored with
            //    a weaker consistency level. A replay might "see" the deleted events before it sees the stored
            //    snapshot, i.e. it could use an older snapshot and not replay the full sequence of events
            // 2. If there is a production failure, it's useful to be able to inspect the events while debugging
            var sequenceNr = e.Metadata.SequenceNr - keepNrOfBatches * snapshotAfter;
            if (sequenceNr > 0)
                DeleteMessages(sequenceNr);
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
                Log.Error(reason, "Exception in ReceiveRecover when replaying event type [{0}] with sequence number [{1}] for persistenceId [{2}]",
                    message.GetType(), LastSequenceNr, PersistenceId);
            }
            else
            {
                Log.Error(reason, "Persistence failure when replaying events for persistenceId [{0}]. Last known sequence number [{1}]", PersistenceId, LastSequenceNr);
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
            Log.Error(cause, "Failed to persist event type [{0}] with sequence number [{1}] for persistenceId [{2}].",
                @event.GetType(), sequenceNr, PersistenceId);
        }

        /// <summary>
        /// Called when the journal rejected <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/> of an event.
        /// The event was not stored. By default this method logs the problem as an error, and the actor continues.
        /// The callback handler that was passed to the <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/>
        /// method will not be invoked.
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="event">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        protected virtual void OnPersistRejected(Exception cause, object @event, long sequenceNr)
        {
            Log.Error(cause, "Rejected to persist event type [{0}] with sequence number [{1}] for persistenceId [{2}] due to [{3}].",
                @event.GetType(), sequenceNr, PersistenceId, cause.Message);
        }

        /// <summary>
        /// Runs an asynchronous task for incoming messages in context of <see cref="ReceiveCommand(object)"/> .
        /// <remarks>The actor will be suspended until the task returned by <paramref name="action"/> completes, including the <see cref="Eventsourced.Persist{TEvent}(TEvent, Action{TEvent})" />
        /// and <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent}, Action{TEvent})" /> calls.</remarks>
        /// </summary>
        /// <param name="action">Async task to run</param>
        protected void RunTask(Func<Task> action)
        {
            if (_asyncTaskRunning)
                throw new NotSupportedException("RunTask calls cannot be nested");
            Func<Task> wrap = () =>
            {
                Task t = action();
                if (!t.IsCompleted)
                {
                    _asyncTaskRunning = true;
                    var tcs = new TaskCompletionSource<object>();

                    t.ContinueWith(r =>
                    {
                        _asyncTaskRunning = false;

                        OnProcessingCommandsAroundReceiveComplete(r.IsFaulted || r.IsCanceled);

                        if (r.IsFaulted)
                            tcs.TrySetException(r.Exception);
                        else if (r.IsCanceled)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetResult(null);
                    }, TaskContinuationOptions.ExecuteSynchronously);

                    t = tcs.Task;
                }
                return t;
            };

            Dispatch.ActorTaskScheduler.RunTask(wrap);
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
                Journal.Tell(new WriteMessages(_journalBatch, Self, _instanceId));
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
            catch (StashOverflowException)
            {
                var strategy = InternalStashOverflowStrategy;
                if (strategy is DiscardToDeadLetterStrategy)
                {
                    var sender = Sender;
                    Context.System.DeadLetters.Tell(new DeadLetter(currentMessage, sender, Self), Sender);
                }
                else if (strategy is ReplyToStrategy toStrategy)
                {
                    Sender.Tell(toStrategy.Response);
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

            public int Count => _userStash.Count;
            public bool IsEmpty => _userStash.IsEmpty;
            public bool NonEmpty => _userStash.NonEmpty;
            public bool IsFull => _userStash.IsFull;
            public int Capacity => _userStash.Capacity;
        }
    }
}
