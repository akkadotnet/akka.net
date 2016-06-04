//-----------------------------------------------------------------------
// <copyright file="JournalDbEngine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Class used for storing intermediate result of the <see cref="IPersistentRepresentation"/>
    /// in form which is ready to be stored directly in the SQL table.
    /// </summary>
    public sealed class JournalEntry
    {
        public readonly string PersistenceId;
        public readonly long SequenceNr;
        public readonly bool IsDeleted;
        public readonly string Manifest;
        public readonly DateTime Timestamp;
        public readonly object Payload;

        public JournalEntry(string persistenceId, long sequenceNr, bool isDeleted, string manifest, DateTime timestamp, object payload)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            IsDeleted = isDeleted;
            Manifest = manifest;
            Payload = payload;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Persisted event identifier returning set of keys used to map particular instance of an event to database row id.
    /// </summary>
    public struct EventId
    {
        /// <summary>
        /// Database row identifier.
        /// </summary>
        public readonly long Id;

        /// <summary>
        /// Persistent event sequence number, monotonically increasing in scope of the same <see cref="PersistenceId"/>.
        /// </summary>
        public readonly long SequenceNr;

        /// <summary>
        /// Id of persistent actor, used to recognize all events related to that actor.
        /// </summary>
        public readonly string PersistenceId;

        public EventId(long id, long sequenceNr, string persistenceId)
        {
            Id = id;
            SequenceNr = sequenceNr;
            PersistenceId = persistenceId;
        }
    }

    public struct WriteTag
    {
        public readonly string Tag;
        public readonly long SequenceNr;

        public WriteTag(string tag, long sequenceNr)
        {
            Tag = tag;
            SequenceNr = sequenceNr;
        }
    }

    /// <summary>
    /// Class used for storing whole intermediate set of write changes to be applied within single SQL transaction.
    /// </summary>
    public sealed class WriteJournalBatch
    {
        public readonly IDictionary<IPersistentRepresentation, IEnumerable<WriteTag>> EntryTags;

        public WriteJournalBatch(IDictionary<IPersistentRepresentation, IEnumerable<WriteTag>> entryTags)
        {
            EntryTags = entryTags;
        }
    }

    /// <summary>
    /// Message type containing set of all <see cref="Eventsourced.PersistenceId"/> received from the database.
    /// </summary>
    public sealed class AllPersistenceIds
    {
        public readonly ImmutableArray<string> Ids;

        public AllPersistenceIds(ImmutableArray<string> ids)
        {
            Ids = ids;
        }
    }
}