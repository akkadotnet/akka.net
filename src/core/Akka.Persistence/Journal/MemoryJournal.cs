//-----------------------------------------------------------------------
// <copyright file="MemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
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
    using Messages = IDictionary<string, ImmutableList<IPersistentRepresentation>>;
    
    /// <summary>
    /// In-memory journal for testing purposes.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        private ImmutableList<IPersistentRepresentation> _allMessages = ImmutableList<IPersistentRepresentation>.Empty;
        private readonly ConcurrentDictionary<string, long> _meta = new();
        private readonly ConcurrentDictionary<string, ImmutableList<IPersistentRepresentation>> _tagsToMessagesMapping = new();
        
        protected virtual ConcurrentDictionary<string, ImmutableList<IPersistentRepresentation>> Messages { get; } = new();
        private readonly ILoggingAdapter _log;

        public MemoryJournal()
        {
            _log = Context.GetLogger();
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                {
                    var persistentRepresentation = p.WithTimestamp(DateTime.UtcNow.Ticks);
                    Add(persistentRepresentation);
                    _allMessages = _allMessages.Add(persistentRepresentation);
                    
                    if (p.Payload is not Tagged tagged)
                    {
                        _log.Info("Message written to memory journal: {0}", p.Payload.ToString());
                        continue;
                    }
                        
                    foreach (var tag in tagged.Tags)
                    {
                        _tagsToMessagesMapping.AddOrUpdate(
                            tag,
                            _ => ImmutableList<IPersistentRepresentation>.Empty.Add(persistentRepresentation),
                            (_, v) => v.Add(persistentRepresentation));
                    }
                    _log.Info("Tagged message written to memory journal: {0}", tagged.Payload.ToString());
                }
            }
            
            return Task.FromResult<IImmutableList<Exception>>(null); // all good
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            return Task.FromResult(Math.Max(HighestSequenceNr(persistenceId), _meta.GetValueOrDefault(persistenceId, 0L)));
        }
        
        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            if (highest != 0L && max != 0L)
                Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max).ForEach(recoveryCallback);
            return Task.CompletedTask;
        }
        
        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            var highestSeqNr = HighestSequenceNr(persistenceId);
            var toSeqNr = Math.Min(toSequenceNr, highestSeqNr);
            if (toSeqNr == highestSeqNr)
                _meta.AddOrUpdate(persistenceId, highestSeqNr, (_, _) => highestSeqNr);
            for (var snr = 1L; snr <= toSeqNr; snr++)
                Delete(persistenceId, snr);
            return Task.CompletedTask;
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
            return Task.FromResult<(IEnumerable<string> Ids, int LastOrdering)>((new HashSet<string>(_allMessages.Skip(offset).Select(p => p.PersistenceId)), _allMessages.Count)); 
        }
        
        /// <summary>
        /// Replays all events with given tag withing provided boundaries from memory.
        /// </summary>
        private Task<int> ReplayTaggedMessagesAsync(ReplayTaggedMessages replay)
        {
            if (!_tagsToMessagesMapping.TryGetValue(replay.Tag, out var taggedMessages))
                return Task.FromResult(0);

            var index = 0;
            // Respect Max and ToOffset boundaries to avoid flooding the subscriber.
            // Use long math to avoid overflow when ToOffset == int.MaxValue.
            var available = Math.Max(0L, (long)replay.ToOffset - replay.FromOffset + 1L);
            var toTake = (int)Math.Min(available, replay.Max);
            foreach (var persistence in taggedMessages
                         .Skip(replay.FromOffset)
                         .Take(toTake))
            {
                replay.ReplyTo.Tell(new ReplayedTaggedMessage(persistence, replay.Tag, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }
            
            return Task.FromResult(taggedMessages.Count - 1);
        }
        
        private Task<int> ReplayAllEventsAsync(ReplayAllEvents replay)
        {
            var index = 0;
            var allMessages = _allMessages;
            // Respect Max and ToOffset boundaries and avoid overflow
            var available = Math.Max(0L, (long)replay.ToOffset - replay.FromOffset + 1L);
            var toTake = (int)Math.Min(available, replay.Max);
            var replayed = allMessages
                .Skip(replay.FromOffset)
                .Take(toTake)
                .ToArray();
            foreach (var message in replayed)
            {
                replay.ReplyTo.Tell(new ReplayedEvent(message, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }
            return Task.FromResult(allMessages.Count - 1);
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
        public sealed class CurrentPersistenceIds
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
        public sealed class ReplayedTaggedMessage : INoSerializationVerificationNeeded
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
        public sealed class ReplayedEvent : INoSerializationVerificationNeeded
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
        
        #region IMemoryMessages implementation
        
        public Messages Add(IPersistentRepresentation persistent)
        {
            Messages.AddOrUpdate(
                persistent.PersistenceId, 
                _ => ImmutableList<IPersistentRepresentation>.Empty.Add(persistent),
                (_, v) => v.Add(persistent));
            return Messages;
        }
        
        public Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater)
        {
            if (!Messages.TryGetValue(pid, out var persistents))
                return Messages;
            
            var list = persistents.Select(updater).ToImmutableList();
            Messages[pid] = list;

            return Messages;
        }
        
        public Messages Delete(string pid, long seqNr)
        {
            if (!Messages.TryGetValue(pid, out var persistents))
                return Messages;

            var list = persistents.Where(node => node.SequenceNr != seqNr).ToImmutableList();
            Messages[pid] = list;

            return Messages;
        }
        
        public IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max)
        {
            if (Messages.TryGetValue(pid, out var persistents))
            {
                return persistents
                    .Where(x => x.SequenceNr >= fromSeqNr && x.SequenceNr <= toSeqNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);
            }

            return [];
        }
        
        public long HighestSequenceNr(string pid)
        {
            if (Messages.TryGetValue(pid, out var persistents))
            {
                var last = persistents.LastOrDefault();
                return last?.SequenceNr ?? 0L;
            }

            return 0L;
        }

        #endregion
    }
    
    public class SharedMemoryJournal : MemoryJournal
    {
        private static readonly ConcurrentDictionary<string, ImmutableList<IPersistentRepresentation>> SharedMessages = new();

        protected override ConcurrentDictionary<string, ImmutableList<IPersistentRepresentation>> Messages => SharedMessages;
    }
}

