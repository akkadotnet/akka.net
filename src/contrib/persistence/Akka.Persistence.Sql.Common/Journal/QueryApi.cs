//-----------------------------------------------------------------------
// <copyright file="QueryApi.cs" company="Akka.NET Project">
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

    /// <summary>
    /// Subscribe the `sender` to current and new persistenceIds.
    /// Used by query-side. The journal will send one <see cref="CurrentPersistenceIds"/> to the
    /// subscriber followed by <see cref="PersistenceIdAdded"/> messages when new persistenceIds
    /// are created.
    /// </summary>
    [Serializable]
    public sealed class SubscribeAllPersistenceIds : ISubscriptionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SubscribeAllPersistenceIds Instance = new SubscribeAllPersistenceIds();
        private SubscribeAllPersistenceIds() { }
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="allPersistenceIds">TBD</param>
        public CurrentPersistenceIds(IEnumerable<string> allPersistenceIds)
        {
            AllPersistenceIds = allPersistenceIds.ToImmutableHashSet();
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class PersistenceIdAdded : IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        public PersistenceIdAdded(string persistenceId)
        {
            PersistenceId = persistenceId;
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