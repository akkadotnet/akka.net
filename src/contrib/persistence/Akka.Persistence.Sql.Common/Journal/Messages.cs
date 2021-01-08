//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long SequenceNr;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsDeleted;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Manifest;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly DateTime Timestamp;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object Payload;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="isDeleted">TBD</param>
        /// <param name="manifest">TBD</param>
        /// <param name="timestamp">TBD</param>
        /// <param name="payload">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="persistenceId">TBD</param>
        public EventId(long id, long sequenceNr, string persistenceId)
        {
            Id = id;
            SequenceNr = sequenceNr;
            PersistenceId = persistenceId;
        }
    }

    /// <summary>
    /// Class used for storing whole intermediate set of write changes to be applied within single SQL transaction.
    /// </summary>
    public sealed class WriteJournalBatch
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IDictionary<IPersistentRepresentation, IImmutableSet<string>> EntryTags;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="entryTags">TBD</param>
        public WriteJournalBatch(IDictionary<IPersistentRepresentation, IImmutableSet<string>> entryTags)
        {
            EntryTags = entryTags;
        }
    }

    /// <summary>
    /// Message type containing set of all <see cref="Eventsourced.PersistenceId"/> received from the database.
    /// </summary>
    public sealed class AllPersistenceIds
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ImmutableArray<string> Ids;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ids">TBD</param>
        public AllPersistenceIds(ImmutableArray<string> ids)
        {
            Ids = ids;
        }
    }
}
