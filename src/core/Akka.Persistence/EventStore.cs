using System.Threading.Tasks;
using Akka.Persistence;
using System.Collections.Generic;
using Akka.Util;
using System;
using System.Collections.Immutable;
using System.Threading;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// Event store component, that can be embedded into custom actors. It doesn't provide any 
    /// thread safety guarantees and should by no means be called from multiple threads.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>
        /// Id of the persistent entity for which messages should be replayed.
        /// </summary>
        string PersistenceId { get; }

        /// <summary>
        /// Highest received sequence number so far or `0L` if this actor 
        /// hasn't replayed  or stored any persistent events yet.
        /// </summary>
        long LastSequenceNr { get; set; }

        #region snapshot API

        /// <summary>
        /// Instructs the event store to load the specified snapshot and send it via an
        /// <see cref="SnapshotOffer"/> to the running <see cref="PersistentActor"/>.
        /// </summary>
        /// <param name="criteria">
        /// Selection criteria used to filter concrete snapshot instance. 
        /// <see cref="SnapshotSelectionCriteria.Latest"/> by default.
        /// </param>
        Task<SnapshotOffer> LoadSnapshot(SnapshotSelectionCriteria criteria = null, long toSequenceNr = long.MaxValue, string persistenceId = null, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously saves <paramref name="snapshot"/>. It cannot be null.
        /// </summary>
        Task SaveSnapshot<TSnapshot>(TSnapshot snapshot, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously deletes a snapshot identified by <paramref name="sequenceNr"/>.
        /// </summary>
        /// <returns></returns>
        Task DeleteSnapshot(long sequenceNr, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously deletes all snapshot within a range of provided snapshot selection 
        /// <paramref name="criteria"/>.
        /// </summary>
        /// <param name="criteria"></param>
        /// <returns></returns>
        Task DeleteSnapshots(SnapshotSelectionCriteria criteria, CancellationToken cancellation = default(CancellationToken));

        #endregion

        #region event API

        /// <summary>
        /// Returns an asynchronous enumerator that can be used to replay a collection of events
        /// fitting into boundaries set by <paramref name="fromSequenceNr"/> and <paramref name="toSequenceNr"/>.
        /// </summary>
        Task ReplayEvents<T>(long fromSequenceNr, long toSequenceNr, int max, Action<T> handler, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously persists an <paramref name="event"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="event"></param>
        /// <returns></returns>
        Task PersistEvent<T>(T @event, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously persists series of <paramref name="events"/> in specified order.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="events"></param>
        /// <returns></returns>
        Task PersistAllEvents<T>(IEnumerable<T> events, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        /// Asynchronously and permanently deletes all persistent messages with sequence 
        /// numbers less than or equal <paramref name="toSequenceNr"/>.
        /// </summary>
        /// <param name="toSequenceNr"></param>
        /// <returns></returns>
        Task DeleteEvents(long toSequenceNr, CancellationToken cancellation = default(CancellationToken));

        #endregion
    }

    internal sealed class EventStore : IEventStore, IActorRef
    {
        private readonly IActorRef _eventJournal;
        private readonly IActorRef _snapshotStore;
        private readonly Persistence _persistence;
        private readonly string _writerGuid;

        private readonly Dictionary<object, TaskCompletionSource<object>> _pendingRequests = new Dictionary<object, TaskCompletionSource<object>>();

        EventStore(Persistence persistence, string persistenceId, IActorRef eventJournal, IActorRef snapshotStore)
        {
            if (string.IsNullOrEmpty(persistenceId)) throw new ArgumentNullException(nameof(persistenceId), "PersistenceId cannot be empty.");

            PersistenceId = persistenceId;
            LastSequenceNr = 0;

            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence), $"Persistence plugin was not initialized.");
            _eventJournal = eventJournal ?? throw new ArgumentNullException(nameof(eventJournal), $"No event journal was provided for event store with persistence id [{persistenceId}].");
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore), $"No snapshot store was provided for event store with persistence id [{persistenceId}].");
            _writerGuid = Guid.NewGuid().ToString();
        }

        #region IEventStore interface

        public string PersistenceId { get; }

        public long LastSequenceNr { get; set; }

        public Task DeleteEvents(long toSequenceNr, CancellationToken cancellation = default(CancellationToken))
        {
            var correlationId = CreateCorrelationId();
            _eventJournal.Tell(new DeleteMessagesTo(PersistenceId, toSequenceNr, this, correlationId), this);
            return SetupCompletion(correlationId, cancellation);
        }

        public Task DeleteSnapshot(long sequenceNr, CancellationToken cancellation = default(CancellationToken))
        {
            var correlationId = CreateCorrelationId();
            _snapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(PersistenceId, sequenceNr), correlationId), this);
            return SetupCompletion(correlationId, cancellation);
        }

        public Task DeleteSnapshots(SnapshotSelectionCriteria criteria, CancellationToken cancellation = default(CancellationToken))
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria), $"Cannot delete snapshots for persistence id [{PersistenceId}] since no criteria were provided.");

            var correlationId = CreateCorrelationId();
            _snapshotStore.Tell(new DeleteSnapshots(PersistenceId, criteria, correlationId), this);
            return SetupCompletion(correlationId, cancellation);
        }

        public async Task<SnapshotOffer> LoadSnapshot(SnapshotSelectionCriteria criteria = null, long toSequenceNr = long.MaxValue, string persistenceId = null, CancellationToken cancellation = default(CancellationToken))
        {
            var correlationId = CreateCorrelationId();
            _snapshotStore.Tell(new LoadSnapshot(
                persistenceId ?? PersistenceId, criteria ?? SnapshotSelectionCriteria.Latest,
                toSequenceNr,
                correlationId), this);

            var result = await SetupCompletion(correlationId, cancellation);
            return result as SnapshotOffer;
        }

        public Task PersistAllEvents<T>(IEnumerable<T> events, CancellationToken cancellation = default(CancellationToken))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            var persistents = ImmutableList<IPersistentRepresentation>.Empty.ToBuilder();
            foreach (var e in events)
            {
                var persistent = new Persistent(e, persistenceId: PersistenceId, sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: ActorRefs.NoSender);
                persistents.Add(persistent);
            }

            var write = new AtomicWrite(persistents.ToImmutable());

            var correlationId = CreateCorrelationId();
            _eventJournal.Tell(new WriteMessages(new IPersistentEnvelope[] { write }, this, 0, correlationId), this);

            return SetupCompletion(correlationId, cancellation);
        }

        public Task PersistEvent<T>(T e, CancellationToken cancellation = default(CancellationToken))
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            var write = new AtomicWrite(new Persistent(e, persistenceId: PersistenceId,
                sequenceNr: NextSequenceNr(), writerGuid: _writerGuid, sender: ActorRefs.NoSender));

            var correlationId = CreateCorrelationId();
            _eventJournal.Tell(new WriteMessages(new IPersistentEnvelope[]{ write }, this, 0, correlationId), this);

            return SetupCompletion(correlationId, cancellation);
        }

        public Task ReplayEvents<T>(long fromSequenceNr, long toSequenceNr, int max, Action<T> handler, CancellationToken cancellation = default(CancellationToken))
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var correlationId = CreateCorrelationId();
            _eventJournal.Tell(new ReplayMessages(fromSequenceNr + 1L, toSequenceNr, max, PersistenceId, this, correlationId), this);

            return SetupCompletion(correlationId, cancellation);
        }

        public Task SaveSnapshot<TSnapshot>(TSnapshot snapshot, CancellationToken cancellation = default(CancellationToken))
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var correlationId = CreateCorrelationId();
            _snapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(PersistenceId, LastSequenceNr), snapshot, correlationId), this);
            return SetupCompletion(correlationId, cancellation);
        }

        #endregion

        #region IActorRef

        ActorPath IActorRef.Path => throw new NotImplementedException();

        int IComparable<IActorRef>.CompareTo(IActorRef other)
        {
            if (other is EventStore es)
            {
                return string.Compare(this.PersistenceId, es.PersistenceId);
            }
            return -1;
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is EventStore es)
            {
                return string.Compare(this.PersistenceId, es.PersistenceId);
            }
            return -1;
        }

        bool IEquatable<IActorRef>.Equals(IActorRef other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (other is EventStore es)
            {
                return this.PersistenceId == es.PersistenceId;
            }
            return false;
        }

        void ICanTell.Tell(object message, IActorRef sender)
        {
            switch (message)
            {
                case IPersistentRepresentation envelope: break;
                case ReplayedMessage replayed: break;
                case RecoverySuccess success: break;
                case ReplayMessagesFailure failure: break;

                case WriteMessageSuccess success: break;
                case WriteMessageRejected rejected: break;
                case WriteMessageFailure failure: break;
                case WriteMessagesSuccessful _: break;
                case WriteMessagesFailed failed: break;

                case LoadSnapshotResult success:
                    {
                        var completion = GetCompletion(success.CorrelationId);
                        if (completion != null)
                        {
                            var offer = success.Snapshot != null
                                ? new SnapshotOffer(success.Snapshot.Metadata, success.Snapshot.Snapshot)
                                : null;

                            completion.TrySetResult(offer);
                        }
                        break;
                    }
                case LoadSnapshotFailed failure:
                    {
                        var completion = GetCompletion(failure.CorrelationId);
                        completion?.TrySetException(failure.Cause);
                        break;
                    }

                case SaveSnapshotSuccess success:
                    {
                        var completion = GetCompletion(success.CorrelationId);
                        completion?.TrySetResult(0);
                        break;
                    }
                case SaveSnapshotFailure failure:
                    {
                        var completion = GetCompletion(failure.CorrelationId);
                        completion?.TrySetException(failure.Cause);
                        break;
                    }

                case DeleteSnapshotSuccess success: break;
                case DeleteSnapshotFailure failure: break;
                case DeleteSnapshotsSuccess success: break;
                case DeleteSnapshotsFailure failure: break;

                case DeleteMessagesSuccess success: break;
                case DeleteMessagesFailure failure: break;

                case RecoveryCompleted _: break;
            }
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system) =>
            throw new NotSupportedException("EventStore instance serialization is not supported.");

        #endregion

        private long NextSequenceNr() => (++LastSequenceNr);
        private int CreateCorrelationId() => ThreadLocalRandom.Current.Next();

        private Task<object> SetupCompletion(object correlationId, CancellationToken token)
        {
            var completion = new TaskCompletionSource<object>();
            _pendingRequests.Add(correlationId, completion);
            if (token != CancellationToken.None)
            {
                token.Register(() =>
                {
                    if (_pendingRequests.Remove(correlationId))
                        completion.TrySetCanceled();
                });
            }

            return completion.Task;
        }

        private TaskCompletionSource<object> GetCompletion(object correlationId)
        {
            if (_pendingRequests.TryGetValue(correlationId, out var completion))
            {
                return completion;
            }
            else return null;
        }
    }
}