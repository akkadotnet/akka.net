//-----------------------------------------------------------------------
// <copyright file="QueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Flattened and serialized snapshot object used as intermediate representation 
    /// before saving snapshot with metadata inside SQL Server database.
    /// </summary>
    public class SnapshotEntry
    {
        /// <summary>
        /// Persistence identifier of persistent actor, current snapshot relates to.
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// Sequence number used to identify snapshot in it's persistent actor scope.
        /// </summary>
        public readonly long SequenceNr;

        /// <summary>
        /// Timestamp used to specify date, when the snapshot has been made.
        /// </summary>
        public readonly DateTime Timestamp;

        /// <summary>
        /// Stringified fully qualified CLR type name of the serialized object.
        /// </summary>
        public readonly string SnapshotType;

        /// <summary>
        /// Serialized object data.
        /// </summary>
        public readonly byte[] Snapshot;

        public SnapshotEntry(string persistenceId, long sequenceNr, DateTime timestamp, string snapshotType, byte[] snapshot)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
            SnapshotType = snapshotType;
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// Query builder used for prepare SQL commands used for snapshot store persistence operations.
    /// </summary>
    public interface ISnapshotQueryBuilder
    {
        /// <summary>
        /// Deletes a single snapshot identified by it's persistent actor's <paramref name="persistenceId"/>, 
        /// <paramref name="sequenceNr"/> and <paramref name="timestamp"/>.
        /// </summary>
        DbCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp);

        /// <summary>
        /// Deletes all snapshot matching persistent actor's <paramref name="persistenceId"/> as well as 
        /// upper (inclusive) bounds of the both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// </summary>
        DbCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        /// <summary>
        /// Inserts a single snapshot represented by provided <see cref="SnapshotEntry"/> instance.
        /// </summary>
        DbCommand InsertSnapshot(SnapshotEntry entry);

        /// <summary>
        /// Selects a single snapshot identified by persistent actor's <paramref name="persistenceId"/>,
        /// matching upper (inclusive) bounds of both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// In case, when more than one snapshot matches specified criteria, one with the highest sequence number will be selected.
        /// </summary>
        DbCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp);
    }

}