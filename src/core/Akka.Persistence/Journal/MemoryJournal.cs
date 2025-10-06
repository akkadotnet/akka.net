//-----------------------------------------------------------------------
// <copyright file="MemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// In-memory journal for testing purposes.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        /// <summary>
        /// All events in append-only order (for AllEvents queries).
        /// </summary>
        private readonly List<IPersistentRepresentation> _eventLog = new();

        /// <summary>
        /// Events indexed by persistence ID for O(1) recovery lookup.
        /// Maintained on write to avoid O(n) scans across all entities during recovery.
        /// </summary>
        private readonly Dictionary<string, List<IPersistentRepresentation>> _eventsByPersistenceId = new();

        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
        private readonly Dictionary<string, long> _deletedTo = new();

        protected virtual List<IPersistentRepresentation> EventLog => _eventLog;
        protected virtual Dictionary<string, List<IPersistentRepresentation>> EventsByPersistenceId => _eventsByPersistenceId;
        protected virtual ReaderWriterLockSlim Lock => _lock;
        protected virtual Dictionary<string, long> DeletedTo => _deletedTo;

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            Lock.EnterWriteLock();
            try
            {
                foreach (var w in messages)
                {
                    foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                    {
                        var persistentRepresentation = p.WithTimestamp(DateTime.UtcNow.Ticks);

                        // Maintain both indexes on write
                        EventLog.Add(persistentRepresentation);

                        if (!EventsByPersistenceId.TryGetValue(persistentRepresentation.PersistenceId, out var pidEvents))
                        {
                            pidEvents = new List<IPersistentRepresentation>();
                            EventsByPersistenceId[persistentRepresentation.PersistenceId] = pidEvents;
                        }
                        pidEvents.Add(persistentRepresentation);
                    }
                }
            }
            finally
            {
                Lock.ExitWriteLock();
            }

            return Task.FromResult<IImmutableList<Exception>>(null);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            Lock.EnterReadLock();
            try
            {
                // Use index for O(1) lookup instead of O(n) scan
                if (!EventsByPersistenceId.TryGetValue(persistenceId, out var events) || events.Count == 0)
                    return Task.FromResult(0L);

                var highest = events[events.Count - 1].SequenceNr;

                // Return actual highest sequence number from journal
                // Deletion is logical only - events remain in index
                return Task.FromResult(highest);
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            IPersistentRepresentation[] messages;

            Lock.EnterReadLock();
            try
            {
                // Use index for O(events_for_entity) instead of O(total_events)
                if (!EventsByPersistenceId.TryGetValue(persistenceId, out var pidEvents))
                {
                    messages = Array.Empty<IPersistentRepresentation>();
                }
                else
                {
                    var deletedToSeq = DeletedTo.GetValueOrDefault(persistenceId, 0L);

                    messages = pidEvents
                        .Where(e => e.SequenceNr > deletedToSeq  // Skip deleted messages
                                 && e.SequenceNr >= fromSequenceNr
                                 && e.SequenceNr <= toSequenceNr)
                        .Take(max > int.MaxValue ? int.MaxValue : (int)max)
                        .ToArray();
                }
            }
            finally
            {
                Lock.ExitReadLock();
            }

            // Execute callbacks outside the lock to avoid potential deadlocks
            foreach (var message in messages)
            {
                recoveryCallback(message);
            }

            return Task.CompletedTask;
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            Lock.EnterWriteLock();
            try
            {
                // Track deletion marker instead of actually removing events
                // This is simpler and matches the semantics (logical deletion)
                DeletedTo[persistenceId] = toSequenceNr;
            }
            finally
            {
                Lock.ExitWriteLock();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Add a persistent representation to the journal and return all messages.
        /// </summary>
        public IDictionary<string, LinkedList<IPersistentRepresentation>> Add(IPersistentRepresentation persistent)
        {
            Lock.EnterWriteLock();
            try
            {
                var timestamped = persistent.WithTimestamp(DateTime.UtcNow.Ticks);

                // Maintain both indexes
                EventLog.Add(timestamped);

                if (!EventsByPersistenceId.TryGetValue(timestamped.PersistenceId, out var pidEvents))
                {
                    pidEvents = new List<IPersistentRepresentation>();
                    EventsByPersistenceId[timestamped.PersistenceId] = pidEvents;
                }
                pidEvents.Add(timestamped);
            }
            finally
            {
                Lock.ExitWriteLock();
            }

            // Return view of all messages as LinkedList per persistence ID for API compatibility
            Lock.EnterReadLock();
            try
            {
                return EventsByPersistenceId.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new LinkedList<IPersistentRepresentation>(kvp.Value));
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Delete a message and return all remaining messages.
        /// Public API for compatibility with existing code.
        /// </summary>
        public IDictionary<string, LinkedList<IPersistentRepresentation>> Delete(string pid, long seqNr)
        {
            Lock.EnterWriteLock();
            try
            {
                var currentDeleted = DeletedTo.GetValueOrDefault(pid, 0L);
                DeletedTo[pid] = Math.Max(currentDeleted, seqNr);
            }
            finally
            {
                Lock.ExitWriteLock();
            }

            // Return view of non-deleted messages as LinkedList per persistence ID for API compatibility
            // Use index instead of scanning entire event log
            Lock.EnterReadLock();
            try
            {
                return EventsByPersistenceId.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new LinkedList<IPersistentRepresentation>(
                        kvp.Value.Where(e => e.SequenceNr > DeletedTo.GetValueOrDefault(kvp.Key, 0L))));
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Read messages for a persistence ID within sequence range.
        /// </summary>
        public IEnumerable<IPersistentRepresentation> Read(string pid, long from, long to, long max)
        {
            Lock.EnterReadLock();
            try
            {
                // Use index for O(events_for_entity) instead of O(total_events)
                if (!EventsByPersistenceId.TryGetValue(pid, out var pidEvents))
                    return Array.Empty<IPersistentRepresentation>();

                var deletedToSeq = DeletedTo.GetValueOrDefault(pid, 0L);

                return pidEvents
                    .Where(e => e.SequenceNr > deletedToSeq
                             && e.SequenceNr >= from
                             && e.SequenceNr <= to)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max)
                    .ToArray(); // Materialize under lock
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Get highest sequence number for a persistence ID.
        /// </summary>
        public long HighestSequenceNr(string pid)
        {
            Lock.EnterReadLock();
            try
            {
                // Use index for O(1) lookup instead of O(n) scan
                if (!EventsByPersistenceId.TryGetValue(pid, out var events) || events.Count == 0)
                    return 0L;

                // Return actual highest sequence number from journal
                // Deletion is logical only - events remain in index
                return events[events.Count - 1].SequenceNr;
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case SelectCurrentPersistenceIds request:
                    SelectAllPersistenceIdsAsync(request.Offset)
                        .PipeTo(request.ReplyTo, success: result => new CurrentPersistenceIds(result.Item1, result.LastOrdering));
                    return true;

                case ReplayTaggedMessages replay:
                    ReplayTaggedMessagesAsync(replay)
                        .PipeTo(replay.ReplyTo, success: h => new ReplayTaggedMessagesSuccess(h), failure: e => new ReplayMessagesFailure(e));
                    return true;

                case ReplayAllEvents replay:
                    ReplayAllEventsAsync(replay)
                        .PipeTo(replay.ReplyTo, success: h => new EventReplaySuccess(h),
                            failure: e => new EventReplayFailure(e));
                    return true;

                default:
                    return false;
            }
        }

        private Task<(IEnumerable<string> Ids, int LastOrdering)> SelectAllPersistenceIdsAsync(int offset)
        {
            HashSet<string> ids;
            int count;

            Lock.EnterReadLock();
            try
            {
                ids = new HashSet<string>(EventLog.Skip(offset).Select(p => p.PersistenceId));
                count = EventLog.Count;
            }
            finally
            {
                Lock.ExitReadLock();
            }

            return Task.FromResult<(IEnumerable<string> Ids, int LastOrdering)>((ids, count));
        }

        /// <summary>
        /// Replays all events with given tag within provided boundaries from memory.
        /// </summary>
        private Task<int> ReplayTaggedMessagesAsync(ReplayTaggedMessages replay)
        {
            IPersistentRepresentation[] snapshot;
            int count;

            Lock.EnterReadLock();
            try
            {
                // Scan for events with matching tag
                snapshot = EventLog
                    .Where(e => e.Payload is Tagged tagged && tagged.Tags.Contains(replay.Tag))
                    .Skip(replay.FromOffset)
                    .Take(replay.Max)
                    .ToArray();

                count = EventLog.Count(e => e.Payload is Tagged tagged && tagged.Tags.Contains(replay.Tag));
            }
            finally
            {
                Lock.ExitReadLock();
            }

            // Send messages outside the lock to avoid potential deadlocks
            var index = 0;
            foreach (var persistence in snapshot)
            {
                replay.ReplyTo.Tell(new ReplayedTaggedMessage(persistence, replay.Tag, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }

            return Task.FromResult(count - 1);
        }

        private Task<int> ReplayAllEventsAsync(ReplayAllEvents replay)
        {
            IPersistentRepresentation[] snapshot;
            int count;

            Lock.EnterReadLock();
            try
            {
                snapshot = EventLog
                    .Skip(replay.FromOffset)
                    .Take((int)replay.Max)
                    .ToArray();

                count = EventLog.Count;
            }
            finally
            {
                Lock.ExitReadLock();
            }

            // Send messages outside the lock to avoid potential deadlocks
            var index = 0;
            foreach (var message in snapshot)
            {
                replay.ReplyTo.Tell(new ReplayedEvent(message, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }

            return Task.FromResult(count - 1);
        }

        #region QueryAPI

        [Serializable]
        public sealed class SelectCurrentPersistenceIds : IJournalRequest
        {
            public IActorRef ReplyTo { get; }
            public int Offset { get; }

            public SelectCurrentPersistenceIds(int offset, IActorRef replyTo)
            {
                Offset = offset;
                ReplyTo = replyTo;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class CurrentPersistenceIds : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IEnumerable<string> AllPersistenceIds;

            public readonly int HighestOrderingNumber;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="allPersistenceIds">TBD</param>
            /// <param name="highestOrderingNumber">TBD</param>
            public CurrentPersistenceIds(IEnumerable<string> allPersistenceIds, int highestOrderingNumber)
            {
                AllPersistenceIds = allPersistenceIds.ToImmutableHashSet();
                HighestOrderingNumber = highestOrderingNumber;
            }
        }

        [Serializable]
        public sealed class ReplayTaggedMessages : IJournalRequest
        {
            public readonly int FromOffset;

            public readonly int ToOffset;

            public readonly int Max;

            public readonly string Tag;

            public readonly IActorRef ReplyTo;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReplayTaggedMessages"/> class.
            /// </summary>
            /// <exception cref="ArgumentException">
            /// This exception is thrown for a number of reasons. These include the following:
            /// <ul>
            /// <li>The specified <paramref name="fromOffset"/> is less than zero.</li>
            /// <li>The specified <paramref name="toOffset"/> is less than or equal to zero.</li>
            /// <li>The specified <paramref name="max"/> is less than or equal to zero.</li>
            /// </ul>
            /// </exception>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="tag"/> is null or empty.
            /// </exception>
            public ReplayTaggedMessages(int fromOffset, int toOffset, int max, string tag, IActorRef replyTo)
            {
                if (fromOffset < 0)
                    throw new ArgumentException("From offset may not be a negative number", nameof(fromOffset));
                if (toOffset <= 0) throw new ArgumentException("To offset must be a positive number", nameof(toOffset));
                if (max <= 0)
                    throw new ArgumentException("Maximum number of replayed messages must be a positive number",
                        nameof(max));
                if (string.IsNullOrEmpty(tag))
                    throw new ArgumentNullException(nameof(tag),
                        "Replay tagged messages require a tag value to be provided");

                FromOffset = fromOffset;
                ToOffset = toOffset;
                Max = max;
                Tag = tag;
                ReplyTo = replyTo;
            }
        }

        [Serializable]
        public sealed class ReplayedTaggedMessage : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {

            public readonly IPersistentRepresentation Persistent;

            [Obsolete("If there are tags, they will be stored in the PersistentRepresentation")]
            public readonly string Tag;

            public readonly int Offset;

            public ReplayedTaggedMessage(IPersistentRepresentation persistent, string tag, int offset)
            {
                Persistent = persistent;
#pragma warning disable CS0618 // Type or member is obsolete
                Tag = tag;
#pragma warning restore CS0618 // Type or member is obsolete
                Offset = offset;
            }
        }

        [Serializable]
        public sealed class ReplayAllEvents : IJournalRequest
        {
            public readonly int FromOffset;

            public readonly int ToOffset;

            public readonly long Max;

            public readonly IActorRef ReplyTo;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReplayAllEvents"/> class.
            /// </summary>
            /// <exception cref="ArgumentException">
            /// This exception is thrown for a number of reasons. These include the following:
            /// <ul>
            /// <li>The specified <paramref name="fromOffset"/> is less than zero.</li>
            /// <li>The specified <paramref name="toOffset"/> is less than or equal to zero.</li>
            /// <li>The specified <paramref name="max"/> is less than or equal to zero.</li>
            /// </ul>
            /// </exception>
            public ReplayAllEvents(int fromOffset, int toOffset, long max, IActorRef replyTo)
            {
                if (fromOffset < 0) throw new ArgumentException("From offset may not be a negative number", nameof(fromOffset));
                if (toOffset <= 0) throw new ArgumentException("To offset must be a positive number", nameof(toOffset));
                if (max <= 0) throw new ArgumentException("Maximum number of replayed messages must be a positive number", nameof(max));

                FromOffset = fromOffset;
                ToOffset = toOffset;
                Max = max;
                ReplyTo = replyTo;
            }
        }


        [Serializable]
        public sealed class ReplayedEvent : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {

            public readonly IPersistentRepresentation Persistent;

            public readonly int Offset;

            public ReplayedEvent(IPersistentRepresentation persistent, int offset)
            {
                Persistent = persistent;
                Offset = offset;
            }
        }

        [Serializable]
        public sealed class ReplayTaggedMessagesSuccess
        {
            public ReplayTaggedMessagesSuccess(int highestSequenceNr)
            {
                HighestSequenceNr = highestSequenceNr;
            }

            /// <summary>
            /// Highest stored sequence number.
            /// </summary>
            public int HighestSequenceNr { get; }
        }

        [Serializable]
        public sealed class EventReplaySuccess
        {
            public EventReplaySuccess(int highestSequenceNr)
            {
                HighestSequenceNr = highestSequenceNr;
            }

            /// <summary>
            /// Highest stored sequence number.
            /// </summary>
            public int HighestSequenceNr { get; }

            public bool Equals(EventReplaySuccess other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(HighestSequenceNr, other.HighestSequenceNr);
            }

            public override bool Equals(object obj)
            {
                if (!(obj is EventReplaySuccess evt)) return false;
                return Equals(evt);
            }

            public override int GetHashCode() => HighestSequenceNr.GetHashCode();

            public override string ToString() => $"EventReplaySuccess<highestSequenceNr: {HighestSequenceNr}>";
        }

        public sealed class EventReplayFailure
        {
            public EventReplayFailure(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// Highest stored sequence number.
            /// </summary>
            public Exception Cause { get; }

            public bool Equals(EventReplayFailure other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Cause, other.Cause);
            }


            public override bool Equals(object obj)
            {
                return obj is EventReplayFailure f && Equals(f);
            }


            public override int GetHashCode() => Cause.GetHashCode();


            public override string ToString() => $"EventReplayFailure<cause: {Cause.Message}>";
        }

        #endregion
    }

    public class SharedMemoryJournal : MemoryJournal
    {
        private static readonly List<IPersistentRepresentation> SharedEventLog = new();
        private static readonly Dictionary<string, List<IPersistentRepresentation>> SharedEventsByPersistenceId = new();
        private static readonly ReaderWriterLockSlim SharedLock = new(LockRecursionPolicy.NoRecursion);
        private static readonly Dictionary<string, long> SharedDeletedTo = new();

        protected override List<IPersistentRepresentation> EventLog => SharedEventLog;
        protected override Dictionary<string, List<IPersistentRepresentation>> EventsByPersistenceId => SharedEventsByPersistenceId;
        protected override ReaderWriterLockSlim Lock => SharedLock;
        protected override Dictionary<string, long> DeletedTo => SharedDeletedTo;
    }
}
