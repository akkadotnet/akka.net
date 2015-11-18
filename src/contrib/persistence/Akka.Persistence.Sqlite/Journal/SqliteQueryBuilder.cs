//-----------------------------------------------------------------------
// <copyright file="SqliteQueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sqlite.Journal
{
    internal class SqliteQueryBuilder : IJournalQueryBuilder
    {
        private readonly string _tableName;

        private readonly string _selectHighestSequenceNrSql;
        private readonly string _insertMessagesSql;

        public SqliteQueryBuilder(string tableName)
        {
            _tableName = tableName;
            _insertMessagesSql = string.Format(
                "INSERT INTO {0} (persistence_id, sequence_nr, is_deleted, manifest, timestamp, payload) VALUES (@PersistenceId, @SequenceNr, @IsDeleted, @Manifest, @Timestamp, @Payload)", _tableName);
            _selectHighestSequenceNrSql = string.Format(@"SELECT MAX(sequence_nr) FROM {0} WHERE persistence_id = ? ", _tableName);
        }

        public DbCommand SelectEvents(IEnumerable<IHint> hints)
        {
            var sqlCommand = new SQLiteCommand();

            var sqlized = hints
                .Select(h => HintToSql(h, sqlCommand))
                .Where(x => !string.IsNullOrEmpty(x));

            var where = string.Join(" AND ", sqlized);
            var sql = new StringBuilder("SELECT persistence_id, sequence_nr, is_deleted, manifest, payload FROM " + _tableName);
            if (!string.IsNullOrEmpty(where))
            {
                sql.Append(" WHERE ").Append(where);
            }

            sqlCommand.CommandText = sql.ToString();
            return sqlCommand;
        }

        private string HintToSql(IHint hint, SQLiteCommand command)
        {
            if (hint is TimestampRange)
            {
                var range = (TimestampRange)hint;
                var sb = new StringBuilder();
                
                if (range.From.HasValue)
                {
                    sb.Append(" timestamp >= @TimestampFrom ");
                    command.Parameters.AddWithValue("@TimestampFrom", range.From.Value);
                }
                if (range.From.HasValue && range.To.HasValue) sb.Append("AND");
                if (range.To.HasValue)
                {
                    sb.Append(" timestamp < @TimestampTo ");
                    command.Parameters.AddWithValue("@TimestampTo", range.To.Value);
                }

                return sb.ToString();
            }
            if (hint is PersistenceIdRange)
            {
                var range = (PersistenceIdRange)hint;
                var sb = new StringBuilder(" persistence_id IN (");
                var i = 0;
                foreach (var persistenceId in range.PersistenceIds)
                {
                    var paramName = "@Pid" + (i++);
                    sb.Append(paramName).Append(',');
                    command.Parameters.AddWithValue(paramName, persistenceId);
                }
                return range.PersistenceIds.Count == 0
                    ? string.Empty
                    : sb.Remove(sb.Length - 1, 1).Append(')').ToString();
            }
            else if (hint is WithManifest)
            {
                var manifest = (WithManifest)hint;
                command.Parameters.AddWithValue("@Manifest", manifest.Manifest);
                return " manifest = @Manifest";
            }
            else throw new NotSupportedException(string.Format("Sqlite journal doesn't support query with hint [{0}]", hint.GetType()));
        }

        public DbCommand SelectMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max)
        {
            var sb = new StringBuilder(@"
                SELECT persistence_id, sequence_nr, is_deleted, manifest, payload FROM ").Append(_tableName)
                .Append(" WHERE persistence_id = ? ");

            if (fromSequenceNr > 0)
            {
                if (toSequenceNr != long.MaxValue)
                    sb.Append(" AND sequence_nr BETWEEN ")
                        .Append(fromSequenceNr)
                        .Append(" AND ")
                        .Append(toSequenceNr);
                else
                    sb.Append(" AND sequence_nr >= ").Append(fromSequenceNr);
            }

            if (toSequenceNr != long.MaxValue)
                sb.Append(" AND sequence_nr <= ").Append(toSequenceNr);

            if (max != long.MaxValue)
            {
                sb.Append(" LIMIT ").Append(max);
            }

            var command = new SQLiteCommand(sb.ToString())
            {
                Parameters = { new SQLiteParameter { Value = persistenceId } }
            };

            return command;
        }

        public DbCommand SelectHighestSequenceNr(string persistenceId)
        {
            return new SQLiteCommand(_selectHighestSequenceNrSql)
            {
                Parameters = { new SQLiteParameter { Value = persistenceId } }
            };
        }

        public DbCommand InsertBatchMessages(IPersistentRepresentation[] messages)
        {
            var command = new SQLiteCommand(_insertMessagesSql);
            command.Parameters.Add("@PersistenceId", DbType.String);
            command.Parameters.Add("@SequenceNr", DbType.Int64);
            command.Parameters.Add("@IsDeleted", DbType.Boolean);
            command.Parameters.Add("@Manifest", DbType.String);
            command.Parameters.Add("@Timestamp", DbType.DateTime); 
            command.Parameters.Add("@Payload", DbType.Binary);

            return command;
        }

        public DbCommand DeleteBatchMessages(string persistenceId, long toSequenceNr, bool permanent)
        {
            var sb = new StringBuilder();

            if (permanent)
            {
                sb.Append("DELETE FROM ").Append(_tableName);
            }
            else
            {
                sb.AppendFormat("UPDATE {0} SET is_deleted = 1", _tableName);
            }

            sb.Append(" WHERE persistence_id = ?");

            if (toSequenceNr != long.MaxValue)
            {
                sb.Append(" AND sequence_nr <= ").Append(toSequenceNr);
            }

            return new SQLiteCommand(sb.ToString())
            {
                Parameters = { new SQLiteParameter { Value = persistenceId } }
            };
        }
    }
}