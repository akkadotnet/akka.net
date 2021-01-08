//-----------------------------------------------------------------------
// <copyright file="QueryApi.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ISubscriptionCommand { }

    /// <summary>
    /// Subscribe the `sender` to changes (appended events) for a specific `persistenceId`.
    /// Used by query-side. The journal will send <see cref="EventAppended"/> messages to
    /// the subscriber when <see cref="AsyncWriteJournal.WriteMessagesAsync"/> has been called.
    /// </summary>
    [Serializable]
    public sealed class SubscribePersistenceId : ISubscriptionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        public SubscribePersistenceId(string persistenceId)
        {
            PersistenceId = persistenceId;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class EventAppended : IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        public EventAppended(string persistenceId)
        {
            PersistenceId = persistenceId;
        }
    }

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
    /// Subscribe the `sender` to new appended events.
    /// Used by query-side. The journal will send <see cref="NewEventAppended"/> messages to
    /// the subscriber when `asyncWriteMessages` has been called.
    /// </summary>
    [Serializable]
    public sealed class SubscribeNewEvents : ISubscriptionCommand
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

    /// <summary>
    /// Subscribe the `sender` to changes (appended events) for a specific `tag`.
    /// Used by query-side. The journal will send <see cref="TaggedEventAppended"/> messages to
    /// the subscriber when `asyncWriteMessages` has been called.
    /// Events are tagged by wrapping in <see cref="Tagged"/>
    /// via an <see cref="IEventAdapter"/>.
    /// </summary>
    [Serializable]
    public sealed class SubscribeTag : ISubscriptionCommand
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (!(obj is EventReplaySuccess evt)) return false;
            return Equals(evt);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => HighestSequenceNr.GetHashCode();

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (!(obj is EventReplayFailure f)) return false;
            return Equals(f);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => Cause.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"EventReplayFailure<cause: {Cause.Message}>";
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
            if (fromOffset < 0) throw new ArgumentException("From offset may not be a negative number", nameof(fromOffset));
            if (toOffset <= 0) throw new ArgumentException("To offset must be a positive number", nameof(toOffset));
            if (max <= 0) throw new ArgumentException("Maximum number of replayed messages must be a positive number", nameof(max));
            if (string.IsNullOrEmpty(tag)) throw new ArgumentNullException(nameof(tag), "Replay tagged messages require a tag value to be provided");

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
}
