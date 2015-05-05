using System;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace Akka.Persistence.SqlServer.Snapshot
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
        SqlCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp);

        /// <summary>
        /// Deletes all snapshot matching persistent actor's <paramref name="persistenceId"/> as well as 
        /// upper (inclusive) bounds of the both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// </summary>
        SqlCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        /// <summary>
        /// Inserts a single snapshot represented by provided <see cref="SnapshotEntry"/> instance.
        /// </summary>
        SqlCommand InsertSnapshot(SnapshotEntry entry);

        /// <summary>
        /// Selects a single snapshot identified by persistent actor's <paramref name="persistenceId"/>,
        /// matching upper (inclusive) bounds of both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// In case, when more than one snapshot matches specified criteria, one with the highest sequence number will be selected.
        /// </summary>
        SqlCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp);
    }

    internal class DefaultSnapshotQueryBuilder : ISnapshotQueryBuilder
    {
        private readonly string _deleteSql;
        private readonly string _insertSql;
        private readonly string _selectSql;

        public DefaultSnapshotQueryBuilder(string schemaName, string tableName)
        {
            _deleteSql = @"DELETE FROM {0}.{1} WHERE CS_PID = CHECKSUM(@PersistenceId) ".QuoteSchemaAndTable(schemaName, tableName);
            _insertSql = @"INSERT INTO {0}.{1} (PersistenceId, SequenceNr, Timestamp, SnapshotType, Snapshot) VALUES (@PersistenceId, @SequenceNr, @Timestamp, @SnapshotType, @Snapshot)".QuoteSchemaAndTable(schemaName, tableName);
            _selectSql = @"SELECT PersistenceId, SequenceNr, Timestamp, SnapshotType, Snapshot FROM {0}.{1} WHERE CS_PID = CHECKSUM(@PersistenceId)".QuoteSchemaAndTable(schemaName, tableName);
        }

        public SqlCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            var sqlCommand = new SqlCommand();
            sqlCommand.Parameters.Add(new SqlParameter("@PersistenceId", SqlDbType.NVarChar, persistenceId.Length) { Value = persistenceId });
            var sb = new StringBuilder(_deleteSql);

            if (sequenceNr < long.MaxValue && sequenceNr > 0)
            {
                sb.Append(@"AND SequenceNr = @SequenceNr ");
                sqlCommand.Parameters.Add(new SqlParameter("@SequenceNr", SqlDbType.BigInt) { Value = sequenceNr });
            }

            if (timestamp > DateTime.MinValue && timestamp < DateTime.MaxValue)
            {
                sb.Append(@"AND Timestamp = @Timestamp");
                sqlCommand.Parameters.Add(new SqlParameter("@Timestamp", SqlDbType.DateTime2) { Value = timestamp });
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public SqlCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new SqlCommand();
            sqlCommand.Parameters.Add(new SqlParameter("@PersistenceId", SqlDbType.NVarChar, persistenceId.Length) { Value = persistenceId });
            var sb = new StringBuilder(_deleteSql);

            if (maxSequenceNr < long.MaxValue && maxSequenceNr > 0)
            {
                sb.Append(@" AND SequenceNr <= @SequenceNr ");
                sqlCommand.Parameters.Add(new SqlParameter("@SequenceNr", SqlDbType.BigInt) { Value = maxSequenceNr });
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.Append(@" AND Timestamp <= @Timestamp");
                sqlCommand.Parameters.Add(new SqlParameter("@Timestamp", SqlDbType.DateTime2) { Value = maxTimestamp });
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public SqlCommand InsertSnapshot(SnapshotEntry entry)
        {
            var sqlCommand = new SqlCommand(_insertSql)
            {
                Parameters =
                {
                    new SqlParameter("@PersistenceId", SqlDbType.NVarChar, entry.PersistenceId.Length) { Value = entry.PersistenceId },
                    new SqlParameter("@SequenceNr", SqlDbType.BigInt) { Value = entry.SequenceNr },
                    new SqlParameter("@Timestamp", SqlDbType.DateTime2) { Value = entry.Timestamp },
                    new SqlParameter("@SnapshotType", SqlDbType.NVarChar, entry.SnapshotType.Length) { Value = entry.SnapshotType },
                    new SqlParameter("@Snapshot", SqlDbType.VarBinary, entry.Snapshot.Length) { Value = entry.Snapshot }
                }
            };

            return sqlCommand;
        }

        public SqlCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new SqlCommand();
            sqlCommand.Parameters.Add(new SqlParameter("@PersistenceId", SqlDbType.NVarChar, persistenceId.Length) { Value = persistenceId });

            var sb = new StringBuilder(_selectSql);
            if (maxSequenceNr > 0 && maxSequenceNr < long.MaxValue)
            {
                sb.Append(" AND SequenceNr <= @SequenceNr ");
                sqlCommand.Parameters.Add(new SqlParameter("@SequenceNr", SqlDbType.BigInt) { Value = maxSequenceNr });
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.Append(" AND SequenceNr <= @SequenceNr ");
                sqlCommand.Parameters.Add(new SqlParameter("@Timestamp", SqlDbType.DateTime2) { Value = maxTimestamp });
            }

            sb.Append(" ORDER BY SequenceNr DESC");
            sqlCommand.CommandText = sb.ToString();
            return sqlCommand;
        }
    }
}