//-----------------------------------------------------------------------
// <copyright file="QueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Text;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    internal class QueryBuilder : ISnapshotQueryBuilder
    {
        private readonly string _deleteSql;
        private readonly string _insertSql;
        private readonly string _selectSql;

        public QueryBuilder(SqliteSnapshotSettings settings)
        {
            _deleteSql = string.Format(@"DELETE FROM {0} WHERE persistence_id = ? ", settings.TableName);
            _insertSql = string.Format(@"INSERT INTO {0} (persistence_id, sequence_nr, created_at, manifest, snapshot) VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Snapshot)", settings.TableName);
            _selectSql = string.Format(@"SELECT persistence_id, sequence_nr, created_at, manifest, snapshot FROM {0} WHERE persistence_id = ? ", settings.TableName);
        }

        public DbCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            var sqlCommand = new SQLiteCommand();
            sqlCommand.Parameters.Add(new SQLiteParameter { Value = persistenceId });
            var sb = new StringBuilder(_deleteSql);

            if (sequenceNr < long.MaxValue && sequenceNr > 0)
            {
                sb.Append(@" AND sequence_nr = ").Append(sequenceNr);
            }

            if (timestamp > DateTime.MinValue && timestamp < DateTime.MaxValue)
            {
                sb.AppendFormat(@" AND created_at = {0}", timestamp.Ticks);
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public DbCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new SQLiteCommand();
            sqlCommand.Parameters.Add(new SQLiteParameter { Value = persistenceId });
            var sb = new StringBuilder(_deleteSql);

            if (maxSequenceNr < long.MaxValue && maxSequenceNr > 0)
            {
                sb.Append(@" AND sequence_nr <= ").Append(maxSequenceNr);
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.AppendFormat(@" AND created_at <= {0}", maxTimestamp.Ticks);
            }

            sqlCommand.CommandText = sb.ToString();

            return sqlCommand;
        }

        public DbCommand InsertSnapshot(SnapshotEntry entry)
        {
            var sqlCommand = new SQLiteCommand(_insertSql)
            {
                Parameters =
                {
                    new SQLiteParameter("@PersistenceId", DbType.String, entry.PersistenceId.Length) { Value = entry.PersistenceId },
                    new SQLiteParameter("@SequenceNr", DbType.Int64) { Value = entry.SequenceNr },
                    new SQLiteParameter("@Timestamp", DbType.Int64) { Value = entry.Timestamp.Ticks },
                    new SQLiteParameter("@Manifest", DbType.String, entry.SnapshotType.Length) { Value = entry.SnapshotType },
                    new SQLiteParameter("@Snapshot", DbType.Binary, entry.Snapshot.Length) { Value = entry.Snapshot }
                }
            };

            return sqlCommand;
        }

        public DbCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = new SQLiteCommand();
            sqlCommand.Parameters.Add(new SQLiteParameter { Value = persistenceId });

            var sb = new StringBuilder(_selectSql);
            if (maxSequenceNr > 0 && maxSequenceNr < long.MaxValue)
            {
                sb.Append(" AND sequence_nr <= ").Append(maxSequenceNr);
            }

            if (maxTimestamp > DateTime.MinValue && maxTimestamp < DateTime.MaxValue)
            {
                sb.AppendFormat(" AND created_at <= {0} ", maxTimestamp.Ticks);
            }

            sb.Append(" ORDER BY sequence_nr DESC");
            sqlCommand.CommandText = sb.ToString();
            return sqlCommand;
        }
    }
}