using System.Data;
using System.Data.SqlClient;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Akka.Persistence.Sql.Common.Journal;
using System.Data.Common;

namespace Akka.Persistence.PostgreSql.Journal
{
    internal class PostgreSqlJournalQueryBuilder : IJournalQueryBuilder
    {
        private readonly string _schemaName;
        private readonly string _tableName;

        private readonly string _selectHighestSequenceNrSql;
        private readonly string _insertMessagesSql;

        public PostgreSqlJournalQueryBuilder(string tableName, string schemaName)
        {
            _tableName = tableName;
            _schemaName = schemaName;

            _insertMessagesSql = "INSERT INTO {0}.{1} (persistence_id, sequence_nr, is_deleted, payload_type, payload) VALUES (:persistence_id, :sequence_nr, :is_deleted, :payload_type, :payload)"
                .QuoteSchemaAndTable(_schemaName, _tableName);
            _selectHighestSequenceNrSql = @"SELECT MAX(sequence_nr) FROM {0}.{1} WHERE persistence_id = :persistence_id".QuoteSchemaAndTable(_schemaName, _tableName);
        }

        public DbCommand SelectMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max)
        {
            var sql = BuildSelectMessagesSql(fromSequenceNr, toSequenceNr, max);
            var command = new NpgsqlCommand(sql)
            {
                Parameters = { PersistenceIdToSqlParam(persistenceId) }
            };

            return command;
        }

        public DbCommand SelectHighestSequenceNr(string persistenceId)
        {
            var command = new NpgsqlCommand(_selectHighestSequenceNrSql)
            {
                Parameters = { PersistenceIdToSqlParam(persistenceId) }
            };

            return command;
        }

        public DbCommand InsertBatchMessages(IPersistentRepresentation[] messages)
        {
            var command = new NpgsqlCommand(_insertMessagesSql);
            command.Parameters.Add(":persistence_id", NpgsqlDbType.Varchar);
            command.Parameters.Add(":sequence_nr", NpgsqlDbType.Bigint);
            command.Parameters.Add(":is_deleted", NpgsqlDbType.Boolean);
            command.Parameters.Add(":payload_type", NpgsqlDbType.Varchar);
            command.Parameters.Add(":payload", NpgsqlDbType.Bytea);

            return command;
        }

        public DbCommand DeleteBatchMessages(string persistenceId, long toSequenceNr, bool permanent)
        {
            var sql = BuildDeleteSql(toSequenceNr, permanent);
            var command = new NpgsqlCommand(sql)
            {
                Parameters = { PersistenceIdToSqlParam(persistenceId) }
            };

            return command;
        }

        private string BuildDeleteSql(long toSequenceNr, bool permanent)
        {
            var sqlBuilder = new StringBuilder();

            if (permanent)
            {
                sqlBuilder.Append("DELETE FROM {0}.{1} ".QuoteSchemaAndTable(_schemaName, _tableName));
            }
            else
            {
                sqlBuilder.Append("UPDATE {0}.{1} SET is_deleted = true ".QuoteSchemaAndTable(_schemaName, _tableName));
            }

            sqlBuilder.Append("WHERE persistence_id = :persistence_id");

            if (toSequenceNr != long.MaxValue)
            {
                sqlBuilder.Append(" AND sequence_nr <= ").Append(toSequenceNr);
            }

            var sql = sqlBuilder.ToString();
            return sql;
        }

        private string BuildSelectMessagesSql(long fromSequenceNr, long toSequenceNr, long max)
        {
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendFormat(
                @"SELECT
                    persistence_id,
                    sequence_nr,
                    is_deleted,
                    payload_type,
                    payload ")
                .Append(" FROM {0}.{1} WHERE persistence_id = :persistence_id".QuoteSchemaAndTable(_schemaName, _tableName));

            // since we guarantee type of fromSequenceNr, toSequenceNr and max
            // we can inline them without risk of SQL injection

            if (fromSequenceNr > 0)
            {
                if (toSequenceNr != long.MaxValue)
                    sqlBuilder.Append(" AND sequence_nr BETWEEN ")
                        .Append(fromSequenceNr)
                        .Append(" AND ")
                        .Append(toSequenceNr);
                else
                    sqlBuilder.Append(" AND sequence_nr >= ").Append(fromSequenceNr);
            }

            if (toSequenceNr != long.MaxValue)
                sqlBuilder.Append(" AND sequence_nr <= ").Append(toSequenceNr);

            if (max != long.MaxValue)
            {
                sqlBuilder.AppendFormat(" LIMIT {0}", max);
            }

            var sql = sqlBuilder.ToString();
            return sql;
        }

        private static NpgsqlParameter PersistenceIdToSqlParam(string persistenceId, string paramName = null)
        {
            return new NpgsqlParameter(paramName ?? ":persistence_id", NpgsqlDbType.Varchar, persistenceId.Length) { Value = persistenceId };
        }
    }
}