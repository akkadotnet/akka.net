using System;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Akka.Persistence.Sql.Common.Snapshot;
using System.Data.Common;

namespace Akka.Persistence.PostgreSql.Snapshot
{
    internal class PostgreSqlSnapshotQueryBuilder : ISnapshotQueryBuilder
    {
        private readonly string _deleteSql;
        private readonly string _insertSql;
        private readonly string _selectSql;

        public PostgreSqlSnapshotQueryBuilder(string schemaName, string tableName)
        {
            _deleteSql = @"DELETE FROM {0}.{1} WHERE persistence_id = :persistence_id ".QuoteSchemaAndTable(schemaName, tableName);
            _insertSql = @"INSERT INTO {0}.{1} (persistence_id, sequence_nr, created_at, created_at_ticks, snapshot_type, snapshot) VALUES (:persistence_id, :sequence_nr, :created_at, :created_at_ticks, :snapshot_type, :snapshot)".QuoteSchemaAndTable(schemaName, tableName);
            _selectSql = @"SELECT persistence_id, sequence_nr, created_at, created_at_ticks, snapshot_type, snapshot FROM {0}.{1} WHERE persistence_id = :persistence_id".QuoteSchemaAndTable(schemaName, tableName);
        }

        public DbCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            var sqlCommand = new NpgsqlCommand();
            sqlCommand.Parameters.Add(new NpgsqlParameter(":persistence_id", NpgsqlDbType.Varchar, persistenceId.Length)
            {
                Value = persistenceId
            });
            var sb = new StringBuilder(_deleteSql);

            if (sequenceNr < long.MaxValue && sequenceNr > 0)
            {
                sb.Append(@"AND sequence_nr = :sequence_nr ");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":sequence_nr", NpgsqlDbType.Bigint) {Value = sequenceNr});
            }

            if (timestamp > DateTime.MinValue && timestamp < DateTime.MaxValue)
            {
                sb.Append(@"AND created_at = :created_at AND created_at_ticks = :created_at_ticks");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at", NpgsqlDbType.Timestamp)
                {
                    Value = GetMaxPrecisionTicks(timestamp)
                });
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at_ticks", NpgsqlDbType.Smallint)
                {
                    Value = GetExtraTicks(timestamp)
                });
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public DbCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new NpgsqlCommand();
            sqlCommand.Parameters.Add(new NpgsqlParameter(":persistence_id", NpgsqlDbType.Varchar, persistenceId.Length)
            {
                Value = persistenceId
            });
            var sb = new StringBuilder(_deleteSql);

            if (maxSequenceNr < long.MaxValue && maxSequenceNr > 0)
            {
                sb.Append(@" AND sequence_nr <= :sequence_nr ");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":sequence_nr", NpgsqlDbType.Bigint)
                {
                    Value = maxSequenceNr
                });
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.Append(
                    @" AND (created_at < :created_at OR (created_at = :created_at AND created_at_ticks <= :created_at_ticks)) ");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at", NpgsqlDbType.Timestamp)
                {
                    Value = GetMaxPrecisionTicks(maxTimestamp)
                });
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at_ticks", NpgsqlDbType.Smallint)
                {
                    Value = GetExtraTicks(maxTimestamp)
                });
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public DbCommand InsertSnapshot(SnapshotEntry entry)
        {
            var sqlCommand = new NpgsqlCommand(_insertSql)
            {
                Parameters =
                {
                    new NpgsqlParameter(":persistence_id", NpgsqlDbType.Varchar, entry.PersistenceId.Length) { Value = entry.PersistenceId },
                    new NpgsqlParameter(":sequence_nr", NpgsqlDbType.Bigint) { Value = entry.SequenceNr },
                    new NpgsqlParameter(":created_at", NpgsqlDbType.Timestamp) { Value = GetMaxPrecisionTicks(entry.Timestamp) },
                    new NpgsqlParameter(":created_at_ticks", NpgsqlDbType.Smallint) { Value = GetExtraTicks(entry.Timestamp) },
                    new NpgsqlParameter(":snapshot_type", NpgsqlDbType.Varchar, entry.SnapshotType.Length) { Value = entry.SnapshotType },
                    new NpgsqlParameter(":snapshot", NpgsqlDbType.Bytea, entry.Snapshot.Length) { Value = entry.Snapshot }
                }
            };

            return sqlCommand;
        }

        public DbCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new NpgsqlCommand();
            sqlCommand.Parameters.Add(new NpgsqlParameter(":persistence_id", NpgsqlDbType.Varchar, persistenceId.Length)
            {
                Value = persistenceId
            });

            var sb = new StringBuilder(_selectSql);
            if (maxSequenceNr > 0 && maxSequenceNr < long.MaxValue)
            {
                sb.Append(" AND sequence_nr <= :sequence_nr ");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":sequence_nr", NpgsqlDbType.Bigint)
                {
                    Value = maxSequenceNr
                });
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.Append(
                    @" AND (created_at < :created_at OR (created_at = :created_at AND created_at_ticks <= :created_at_ticks)) ");
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at", NpgsqlDbType.Timestamp)
                {
                    Value = GetMaxPrecisionTicks(maxTimestamp)
                });
                sqlCommand.Parameters.Add(new NpgsqlParameter(":created_at_ticks", NpgsqlDbType.Smallint)
                {
                    Value = GetExtraTicks(maxTimestamp)
                });
            }

            sb.Append(" ORDER BY sequence_nr DESC");
            sqlCommand.CommandText = sb.ToString();
            return sqlCommand;
        }

        private static DateTime GetMaxPrecisionTicks(DateTime date)
        {
            var ticks = (date.Ticks / 10) * 10;

            ticks = date.Ticks - ticks;

            return date.AddTicks(-1 * ticks);
        }

        private static short GetExtraTicks(DateTime date)
        {
            var ticks = date.Ticks;

            return (short)(ticks % 10);
        }
    }
}