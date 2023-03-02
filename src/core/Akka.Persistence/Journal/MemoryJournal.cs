//-----------------------------------------------------------------------
// <copyright file="MemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Persistence.Journal
{
    using Messages = IDictionary<string, LinkedList<IPersistentRepresentation>>;

    /// <summary>
    /// TBD
    /// </summary>
    public interface IMemoryMessages
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <returns>TBD</returns>
        Messages Add(IPersistentRepresentation persistent);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <param name="updater">TBD</param>
        /// <returns>TBD</returns>
        Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <returns>TBD</returns>
        Messages Delete(string pid, long seqNr);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="fromSeqNr">TBD</param>
        /// <param name="toSeqNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <returns>TBD</returns>
        long HighestSequenceNr(string pid);
    }

    /// <summary>
    /// In-memory journal for testing purposes.
    /// </summary>
    public class MemoryJournal : AsyncWriteJournal
    {
        private readonly LinkedList<IPersistentRepresentation> _allMessages = new LinkedList<IPersistentRepresentation>();
        private readonly LinkedList<string> _allPersistenceIds = new LinkedList<string>();
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _messages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();
        private readonly ConcurrentDictionary<string, long> _meta = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> _tagsToMessagesMapping = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();
        private ImmutableDictionary<string, IImmutableSet<IActorRef>> _tagSubscribers = ImmutableDictionary.Create<string, IImmutableSet<IActorRef>>();
        private readonly HashSet<IActorRef> _newEventsSubscriber = new HashSet<IActorRef>();
        
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return _messages; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <returns>TBD</returns>
        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var allTags = new HashSet<string>();
            
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                {
                    var persistentRepresentation = p.WithTimestamp(DateTime.UtcNow.Ticks);
                    Add(persistentRepresentation);
                    _allMessages.AddLast(persistentRepresentation);
                    if (!(p.Payload is Tagged tagged)) continue;
                    
                    foreach (var tag in tagged.Tags)
                    {
                        allTags.UnionWith(tagged.Tags);
                        _tagsToMessagesMapping.AddOrUpdate(
                            tag,
                            (k) => new LinkedList<IPersistentRepresentation>(new[] { persistentRepresentation }),
                            (k, v) =>
                            {
                                v.AddLast(persistentRepresentation);
                                return v;
                            });
                    }
                }
            }
            
            if (!_tagSubscribers.IsEmpty && allTags.Count != 0)
            {
                foreach (var tag in allTags)
                {
                    NotifyTagChange(tag);
                }
            }
            
            if (_newEventsSubscriber.Count != 0)
                NotifyNewEventAppended();
            
            return Task.FromResult((IImmutableList<Exception>) null); // all good
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return Task.FromResult(Math.Max(HighestSequenceNr(persistenceId), _meta.TryGetValue(persistenceId, out long metaSeqNr) ? metaSeqNr : 0L));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="recoveryCallback">TBD</param>
        /// <returns>TBD</returns>
        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            if (highest != 0L && max != 0L)
                Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max).ForEach(recoveryCallback);
            return Task.FromResult(new object());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            var highestSeqNr = HighestSequenceNr(persistenceId);
            var toSeqNr = Math.Min(toSequenceNr, highestSeqNr);
            if (toSeqNr == highestSeqNr)
                _meta.AddOrUpdate(persistenceId, highestSeqNr, (pid, old) => highestSeqNr);
            for (var snr = 1L; snr <= toSeqNr; snr++)
                Delete(persistenceId, snr);
            return Task.FromResult(new object());
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
                        .PipeTo(replay.ReplyTo, success: h => new RecoverySuccess(h), failure: e => new ReplayMessagesFailure(e));
                    return true;
                
                case SubscribeTag subscribe:
                    AddTagSubscriber(Sender, subscribe.Tag);
                    Context.Watch(Sender);
                    return true;
                
                case ReplayAllEvents replay:
                    ReplayAllEventsAsync(replay)
                        .PipeTo(replay.ReplyTo, success: h => new EventReplaySuccess(h),
                            failure: e => new EventReplayFailure(e));
                    return true;
                
                case Terminated terminated:
                    RemoveSubscriber(terminated.ActorRef);
                    return true;
                
                case SubscribeNewEvents _:
                    AddNewEventsSubscriber(Sender);
                    Context.Watch(Sender);
                    return true;
                
                default:
                    return false;
            }
        }
        
        private async Task<(IEnumerable<string> Ids, long LastOrdering)> SelectAllPersistenceIdsAsync(long offset)
        {
            return (_allPersistenceIds.Skip((int)offset), _allPersistenceIds.Count);
        }
        
        /// <summary>
        /// Replays all events with given tag withing provided boundaries from memory.
        /// </summary>
        /// <param name="replay">TBD</param>
        /// <returns>TBD</returns>
        protected async Task<long> ReplayTaggedMessagesAsync(ReplayTaggedMessages replay)
        {
            if (!_tagsToMessagesMapping.ContainsKey(replay.Tag))
                return 0;

            int index = 0;
            foreach (var persistence in _tagsToMessagesMapping[replay.Tag]
                         .Skip((int)replay.FromOffset)
                         .Take(replay.ToOffset > int.MaxValue ? int.MaxValue : (int)replay.ToOffset))
            {
                var payload = (Tagged)persistence.Payload;
                replay.ReplyTo.Tell(new ReplayedTaggedMessage(persistence.WithPayload(payload.Payload), replay.Tag, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }

            return _tagsToMessagesMapping[replay.Tag].Count - 1;
        }
        
        private void AddTagSubscriber(IActorRef subscriber, string tag)
        {
            if (!_tagSubscribers.TryGetValue(tag, out var subscriptions))
            {
                _tagSubscribers = _tagSubscribers.Add(tag, ImmutableHashSet.Create(subscriber));
            }
            else
            {
                _tagSubscribers = _tagSubscribers.SetItem(tag, subscriptions.Add(subscriber));
            }
        }
        
        public void RemoveSubscriber(IActorRef subscriber)
        {
            foreach (var key in _tagSubscribers.Keys)
            {
                _tagSubscribers[key].Remove(subscriber);
            }
            
            _newEventsSubscriber.Remove(subscriber);
        }
        
        public void AddNewEventsSubscriber(IActorRef subscriber)
        {
            _newEventsSubscriber.Add(subscriber);
        }

        private void NotifyTagChange(string tag)
        {
            if (_tagSubscribers.TryGetValue(tag, out var subscribers))
            {
                var changed = new TaggedEventAppended(tag);
                foreach (var subscriber in subscribers)
                    subscriber.Tell(changed);
            }
        }

        private void NotifyNewEventAppended()
        {
            foreach (var subscriber in _newEventsSubscriber)
            {
                subscriber.Tell(NewEventAppended.Instance);
            }
        }
        
        private async Task<long> ReplayAllEventsAsync(ReplayAllEvents replay)
        {
            int index = 0;
            var replayed = _allMessages
                .Skip((int)replay.FromOffset)
                .Take((int)(replay.ToOffset > int.MaxValue
                    ? int.MaxValue - replay.FromOffset
                    : replay.ToOffset - replay.FromOffset))
                .ToArray();
            foreach (var message in replayed)
            {
                replay.ReplyTo.Tell(new ReplayedEvent(message, replay.FromOffset + index), ActorRefs.NoSender);
                index++;
            }

            return _allMessages.Count - 1;
        }
        
        #region QueryAPI

        [Serializable]
        public sealed class SelectCurrentPersistenceIds : IJournalRequest
        {
            public IActorRef ReplyTo { get; }
            public long Offset { get; }

            public SelectCurrentPersistenceIds(long offset, IActorRef replyTo)
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

            public readonly long HighestOrderingNumber;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="allPersistenceIds">TBD</param>
            /// <param name="highestOrderingNumber">TBD</param>
            public CurrentPersistenceIds(IEnumerable<string> allPersistenceIds, long highestOrderingNumber)
            {
                AllPersistenceIds = allPersistenceIds.ToImmutableHashSet();
                HighestOrderingNumber = highestOrderingNumber;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayTaggedMessages : IJournalRequest
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long FromOffset;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly long ToOffset;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly long Max;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Tag;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ReplyTo;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReplayTaggedMessages"/> class.
            /// </summary>
            /// <param name="fromOffset">TBD</param>
            /// <param name="toOffset">TBD</param>
            /// <param name="max">TBD</param>
            /// <param name="tag">TBD</param>
            /// <param name="replyTo">TBD</param>
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
            public ReplayTaggedMessages(long fromOffset, long toOffset, long max, string tag, IActorRef replyTo)
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

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayedTaggedMessage : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IPersistentRepresentation Persistent;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Tag;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long Offset;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="persistent">TBD</param>
            /// <param name="tag">TBD</param>
            /// <param name="offset">TBD</param>
            public ReplayedTaggedMessage(IPersistentRepresentation persistent, string tag, long offset)
            {
                Persistent = persistent;
                Tag = tag;
                Offset = offset;
            }
        }
        
        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class TaggedEventAppended : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Tag;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="tag">TBD</param>
            public TaggedEventAppended(string tag)
            {
                Tag = tag;
            }
        }
        
        /// <summary>
        /// Subscribe the `sender` to changes (appended events) for a specific `tag`.
        /// Used by query-side. The journal will send <see cref="TaggedEventAppended"/> messages to
        /// the subscriber when `asyncWriteMessages` has been called.
        /// Events are tagged by wrapping in <see cref="Tagged"/>
        /// via an <see cref="IEventAdapter"/>.
        /// </summary>
        [Serializable]
        public sealed class SubscribeTag
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Tag;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="tag">TBD</param>
            public SubscribeTag(string tag)
            {
                Tag = tag;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayAllEvents : IJournalRequest
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long FromOffset;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long ToOffset;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long Max;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ReplyTo;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReplayAllEvents"/> class.
            /// </summary>
            /// <param name="fromOffset">TBD</param>
            /// <param name="toOffset">TBD</param>
            /// <param name="max">TBD</param>
            /// <param name="replyTo">TBD</param>
            /// <exception cref="ArgumentException">
            /// This exception is thrown for a number of reasons. These include the following:
            /// <ul>
            /// <li>The specified <paramref name="fromOffset"/> is less than zero.</li>
            /// <li>The specified <paramref name="toOffset"/> is less than or equal to zero.</li>
            /// <li>The specified <paramref name="max"/> is less than or equal to zero.</li>
            /// </ul>
            /// </exception>
            public ReplayAllEvents(long fromOffset, long toOffset, long max, IActorRef replyTo)
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
        
        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayedEvent : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IPersistentRepresentation Persistent;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long Offset;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="persistent">TBD</param>
            /// <param name="tag">TBD</param>
            /// <param name="offset">TBD</param>
            public ReplayedEvent(IPersistentRepresentation persistent, long offset)
            {
                Persistent = persistent;
                Offset = offset;
            }
        }
        
        public sealed class EventReplaySuccess
        {
            public EventReplaySuccess(long highestSequenceNr)
            {
                HighestSequenceNr = highestSequenceNr;
            }

            /// <summary>
            /// Highest stored sequence number.
            /// </summary>
            public long HighestSequenceNr { get; }

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
                if (!(obj is EventReplayFailure f)) return false;
                return Equals(f);
            }

        
            public override int GetHashCode() => Cause.GetHashCode();

        
            public override string ToString() => $"EventReplayFailure<cause: {Cause.Message}>";
        }

        /// <summary>
        /// Subscribe the `sender` to new appended events.
        /// Used by query-side. The journal will send <see cref="NewEventAppended"/> messages to
        /// the subscriber when `asyncWriteMessages` has been called.
        /// </summary>
        [Serializable]
        public sealed class SubscribeNewEvents
        {
            public static SubscribeNewEvents Instance = new SubscribeNewEvents();

            private SubscribeNewEvents() { }
        }
        
        
        [Serializable]
        public sealed class NewEventAppended : IDeadLetterSuppression
        {
            public static NewEventAppended Instance = new NewEventAppended();

            private NewEventAppended() { }
        }
        
        #endregion
        
        #region IMemoryMessages implementation

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <returns>TBD</returns>
        public Messages Add(IPersistentRepresentation persistent)
        {
            if (!Messages.ContainsKey(persistent.PersistenceId))
                _allPersistenceIds.AddLast(persistent.PersistenceId);
            var list = Messages.GetOrAdd(persistent.PersistenceId, pid => new LinkedList<IPersistentRepresentation>());
            list.AddLast(persistent);
            return Messages;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <param name="updater">TBD</param>
        /// <returns>TBD</returns>
        public Messages Update(string pid, long seqNr, Func<IPersistentRepresentation, IPersistentRepresentation> updater)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        node.Value = updater(node.Value);

                    node = node.Next;
                }
            }

            return Messages;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="seqNr">TBD</param>
        /// <returns>TBD</returns>
        public Messages Delete(string pid, long seqNr)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                var node = persistents.First;
                while (node != null)
                {
                    if (node.Value.SequenceNr == seqNr)
                        persistents.Remove(node);

                    node = node.Next;
                }
            }

            return Messages;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <param name="fromSeqNr">TBD</param>
        /// <param name="toSeqNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public IEnumerable<IPersistentRepresentation> Read(string pid, long fromSeqNr, long toSeqNr, long max)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                return persistents
                    .Where(x => x.SequenceNr >= fromSeqNr && x.SequenceNr <= toSeqNr)
                    .Take(max > int.MaxValue ? int.MaxValue : (int)max);
            }

            return Enumerable.Empty<IPersistentRepresentation>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pid">TBD</param>
        /// <returns>TBD</returns>
        public long HighestSequenceNr(string pid)
        {
            if (Messages.TryGetValue(pid, out LinkedList<IPersistentRepresentation> persistents))
            {
                var last = persistents.LastOrDefault();
                return last?.SequenceNr ?? 0L;
            }

            return 0L;
        }

        #endregion
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SharedMemoryJournal : MemoryJournal
    {
        private static readonly ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> SharedMessages = new ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>>();

        /// <summary>
        /// TBD
        /// </summary>
        protected override ConcurrentDictionary<string, LinkedList<IPersistentRepresentation>> Messages { get { return SharedMessages; } }
    }
}

