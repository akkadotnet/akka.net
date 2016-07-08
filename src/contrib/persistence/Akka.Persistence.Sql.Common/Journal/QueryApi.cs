//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Sql.Common.Journal
{
    public interface ISubscriptionCommand { }

    /// <summary>
    /// Subscribe the `sender` to changes (appended events) for a specific `persistenceId`.
    /// Used by query-side. The journal will send <see cref="EventAppended"/> messages to
    /// the subscriber when <see cref="AsyncWriteJournal.WriteMessagesAsync"/> has been called.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class SubscribePersistenceId : ISubscriptionCommand
    {
        public readonly string PersistenceId;

        public SubscribePersistenceId(string persistenceId)
        {
            PersistenceId = persistenceId;
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class EventAppended
    {
        public readonly string PersistenceId;

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
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class SubscribeAllPersistenceIds : ISubscriptionCommand
    {
        public static readonly SubscribeAllPersistenceIds Instance = new SubscribeAllPersistenceIds();
        private SubscribeAllPersistenceIds() { }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class CurrentPersistenceIds
    {
        public readonly IEnumerable<string> AllPersistenceIds;

        public CurrentPersistenceIds(IEnumerable<string> allPersistenceIds)
        {
            AllPersistenceIds = allPersistenceIds.ToImmutableHashSet();
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class PersistenceIdAdded
    {
        public readonly string PersistenceId;

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
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class SubscribeTag : ISubscriptionCommand
    {
        public readonly string Tag;

        public SubscribeTag(string tag)
        {
            Tag = tag;
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class TaggedEventAppended
    {
        public readonly string Tag;

        public TaggedEventAppended(string tag)
        {
            Tag = tag;
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReplayTaggedMessages
    {
        public readonly long FromOffset;
        public readonly long ToOffset;
        public readonly long Max;
        public readonly string Tag;
        public readonly IActorRef ReplyTo;

        public ReplayTaggedMessages(long fromOffset, long toOffset, long max, string tag, IActorRef replyTo)
        {
            if (fromOffset < 0) throw new ArgumentException("From offset may not be a negative number", "fromOffset");
            if (toOffset <= 0) throw new ArgumentException("To offset must be a possitive number", "toOffset");
            if (max <= 0) throw new ArgumentException("Maximum number of replayed messages must be a possitive number", "max");
            if (string.IsNullOrEmpty(tag)) throw new ArgumentNullException("tag", "Replay tagged messages require a tag value to be provided");

            FromOffset = fromOffset;
            ToOffset = toOffset;
            Max = max;
            Tag = tag;
            ReplyTo = replyTo;
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReplayedTaggedMessage : INoSerializationVerificationNeeded
    {
        public readonly IPersistentRepresentation Persistent;
        public readonly string Tag;
        public readonly long Offset;

        public ReplayedTaggedMessage(IPersistentRepresentation persistent, string tag, long offset)
        {
            Persistent = persistent;
            Tag = tag;
            Offset = offset;
        }
    }
}