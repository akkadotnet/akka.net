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
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// In-memory journal for testing purposes.
    ///
    /// Uses a channel-based drain-on-read pattern with immutable collections to handle
    /// the concurrent access pattern imposed by AsyncWriteJournal. Writes enqueue to
    /// an unbounded channel (never blocking), and reads drain the channel first to ensure
    /// all pending writes are visible before returning results.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        /// <summary>
        /// Storage container for journal data. Encapsulates the pending writes channel
        /// and immutable snapshot state.
        /// </summary>
        protected sealed class JournalStorage
        {
            /// <summary>
            /// Lock for serializing drain operations. Reads can happen concurrently
            /// from multiple thread pool threads due to AsyncWriteJournal's fire-and-forget pattern.
            /// </summary>
            internal readonly object DrainLock = new();

            /// <summary>
            /// Pending writes channel — unbounded, writers never block.
            /// </summary>
            internal readonly Channel<IPersistentRepresentation> PendingWrites =
                Channel.CreateUnbounded<IPersistentRepresentation>(
                    new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

            /// <summary>
            /// All events in append-only order (for AllEvents queries).
            /// </summary>
            internal ImmutableList<IPersistentRepresentation> EventLog = ImmutableList<IPersistentRepresentation>.Empty;

            /// <summary>
            /// Events indexed by persistence ID for O(1) recovery lookup.
            /// </summary>
            internal ImmutableDictionary<string, ImmutableList<IPersistentRepresentation>> EventsByPersistenceId =
                ImmutableDictionary<string, ImmutableList<IPersistentRepresentation>>.Empty;

            /// <summary>
            /// Tracks logical deletion markers per persistence ID.
            /// </summary>
            internal ImmutableDictionary<string, long> DeletedTo = ImmutableDictionary<string, long>.Empty;
        }

        private readonly JournalStorage _storage = new();

        /// <summary>
        /// Storage property for accessing journal data. Override in subclasses to share storage.
        /// </summary>
        protected virtual JournalStorage Storage => _storage;

        /// <summary>
        /// Drains all pending writes from the channel into the immutable snapshot state.
        /// Must be called before any read operation to ensure all writes are visible.
        /// </summary>
        private void DrainPendingWrites()
        {
            lock (Storage.DrainLock)
            {
                while (Storage.PendingWrites.Reader.TryRead(out var item))
                {
                    Storage.EventLog = Storage.EventLog.Add(item);

                    var pid = item.PersistenceId;
                    var existing = Storage.EventsByPersistenceId.GetValueOrDefault(
                        pid, ImmutableList<IPersistentRepresentation>.Empty);
                    Storage.EventsByPersistenceId = Storage.EventsByPersistenceId.SetItem(pid, existing.Add(item));
                }
            }
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                {
                    var timestamped = p.WithTimestamp(DateTime.UtcNow.Ticks);
                    // Non-blocking write to channel — TryWrite always succeeds on unbounded channel
                    Storage.PendingWrites.Writer.TryWrite(timestamped);
                }
            }

            return Task.FromResult<IImmutableList<Exception>>(null);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            DrainPendingWrites();

            if (!Storage.EventsByPersistenceId.TryGetValue(persistenceId, out var events) || events.IsEmpty)
                return Task.FromResult(0L);

            return Task.FromResult(events[events.Count - 1].SequenceNr);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            DrainPendingWrites();

            if (Storage.EventsByPersistenceId.TryGetValue(persistenceId, out var pidEvents))
            {
                var deletedToSeq = Storage.DeletedTo.GetValueOrDefault(persistenceId, 0L);

                var messages = pidEvents
                    .Where(e => e.SequenceNr > deletedToSeq
                             && e.SequenceNr >= fromSequenceNr
                             && e.SequenceNr <= toSequenceNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);

                foreach (var message in messages)
                {
                    recoveryCallback(message);
                }
            }

            return Task.CompletedTask;
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            DrainPendingWrites();

            var currentDeleted = Storage.DeletedTo.GetValueOrDefault(persistenceId, 0L);
            Storage.DeletedTo = Storage.DeletedTo.SetItem(persistenceId, Math.Max(currentDeleted, toSequenceNr));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Add a persistent representation to the journal and return all messages.
        /// </summary>
        public IDictionary<string, LinkedList<IPersistentRepresentation>> Add(IPersistentRepresentation persistent)
        {
            var timestamped = persistent.WithTimestamp(DateTime.UtcNow.Ticks);

            // Non-blocking write to channel
            Storage.PendingWrites.Writer.TryWrite(timestamped);

            // Drain and build return value for API compatibility
            DrainPendingWrites();

            return Storage.EventsByPersistenceId.ToDictionary(
                kvp => kvp.Key,
                kvp => new LinkedList<IPersistentRepresentation>(kvp.Value));
        }

        /// <summary>
        /// Delete a message and return all remaining messages.
        /// Public API for compatibility with existing code.
        /// </summary>
        public IDictionary<string, LinkedList<IPersistentRepresentation>> Delete(string pid, long seqNr)
        {
            DrainPendingWrites();

            var currentDeleted = Storage.DeletedTo.GetValueOrDefault(pid, 0L);
            Storage.DeletedTo = Storage.DeletedTo.SetItem(pid, Math.Max(currentDeleted, seqNr));

            return Storage.EventsByPersistenceId.ToDictionary(
                kvp => kvp.Key,
                kvp => new LinkedList<IPersistentRepresentation>(
                    kvp.Value.Where(e => e.SequenceNr > Storage.DeletedTo.GetValueOrDefault(kvp.Key, 0L))));
        }

        /// <summary>
        /// Read messages for a persistence ID within sequence range.
        /// </summary>
        public IEnumerable<IPersistentRepresentation> Read(string pid, long from, long to, long max)
        {
            DrainPendingWrites();

            if (!Storage.EventsByPersistenceId.TryGetValue(pid, out var pidEvents))
                return Array.Empty<IPersistentRepresentation>();

            var deletedToSeq = Storage.DeletedTo.GetValueOrDefault(pid, 0L);

            return pidEvents
                .Where(e => e.SequenceNr > deletedToSeq
                         && e.SequenceNr >= from
                         && e.SequenceNr <= to)
                .Take(max > int.MaxValue ? int.MaxValue : (int)max)
                .ToArray();
        }

        /// <summary>
        /// Get highest sequence number for a persistence ID.
        /// </summary>
        public long HighestSequenceNr(string pid)
        {
            DrainPendingWrites();

            if (!Storage.EventsByPersistenceId.TryGetValue(pid, out var events) || events.IsEmpty)
                return 0L;

            return events[events.Count - 1].SequenceNr;
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
            DrainPendingWrites();

            var ids = new HashSet<string>(Storage.EventLog.Skip(offset).Select(p => p.PersistenceId));
            var count = Storage.EventLog.Count;

            return Task.FromResult<(IEnumerable<string> Ids, int LastOrdering)>((ids, count));
        }

        /// <summary>
        /// Replays all events with given tag within provided boundaries from memory.
        /// </summary>
        private Task<int> ReplayTaggedMessagesAsync(ReplayTaggedMessages replay)
        {
            DrainPendingWrites();

            var snapshot = Storage.EventLog
                .Where(e => e.Payload is Tagged tagged && tagged.Tags.Contains(replay.Tag))
                .Skip(replay.FromOffset)
                .Take(replay.Max)
                .ToArray();

            var count = Storage.EventLog.Count(e => e.Payload is Tagged tagged && tagged.Tags.Contains(replay.Tag));

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
            DrainPendingWrites();

            var snapshot = Storage.EventLog
                .Skip(replay.FromOffset)
                .Take((int)replay.Max)
                .ToArray();

            var count = Storage.EventLog.Count;

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
        private static readonly JournalStorage SharedStorage = new();

        protected override JournalStorage Storage => SharedStorage;
    }
}
