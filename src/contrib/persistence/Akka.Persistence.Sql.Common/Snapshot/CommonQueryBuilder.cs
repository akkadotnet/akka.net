//-----------------------------------------------------------------------
// <copyright file="QueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    internal class CommonQueryBuilder : ISnapshotQueryBuilder
    {
        private readonly DbProviderFactory _dbFactory;

        private readonly string _deleteSql;
        private readonly string _insertSql;
        private readonly string _selectSql;

        public CommonQueryBuilder(DbProviderFactory factory, CommonSnapshotSettings settings)
        {
            _dbFactory = factory;
            _deleteSql = string.Format(@"DELETE FROM {0} WHERE persistence_id = @PersistenceId ", settings.TableName);
            _insertSql = string.Format(@"INSERT INTO {0} (persistence_id, sequence_nr, created_at, manifest, snapshot) VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Snapshot)", settings.TableName);
            _selectSql = string.Format(@"SELECT persistence_id, sequence_nr, created_at, manifest, snapshot FROM {0} WHERE persistence_id = @PersistenceId ", settings.TableName);
        }

        public DbCommand DeleteOne(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            var sqlCommand = _dbFactory.CreateCommand();

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

            var p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            sqlCommand.Parameters.Add(p);

            return sqlCommand;
        }

        public DbCommand DeleteMany(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = _dbFactory.CreateCommand();

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

            var p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            sqlCommand.Parameters.Add(p);

            return sqlCommand;
        }

        public DbCommand InsertSnapshot(SnapshotEntry entry)
        {
            var sqlCommand = _dbFactory.CreateCommand();
            sqlCommand.CommandText = _insertSql;
                   
            
            DbParameter p;

            p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.DbType = DbType.String;
            p.Size = entry.PersistenceId.Length;
            p.Value = entry.PersistenceId;
            sqlCommand.Parameters.Add(p);

            p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@SequenceNr";
            p.DbType = DbType.Int64;
            p.Value = entry.SequenceNr;
            sqlCommand.Parameters.Add(p);

            p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Timestamp";
            p.DbType = DbType.Int64;
            p.Value = entry.Timestamp.Ticks ;
            sqlCommand.Parameters.Add(p);

            p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Manifest";
            p.DbType = DbType.String;
            p.Size = entry.SnapshotType.Length;
            p.Value = entry.SnapshotType;
            sqlCommand.Parameters.Add(p);

            p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Snapshot";
            p.DbType = DbType.Binary;
            p.Size = entry.Snapshot.Length;
            p.Value = entry.Snapshot;
            sqlCommand.Parameters.Add(p);

            return sqlCommand;
        }

        public DbCommand SelectSnapshot(string persistenceId, long maxSequenceNr, DateTime maxTimestamp)
        {
            var sqlCommand = _dbFactory.CreateCommand();
            var p = sqlCommand.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            sqlCommand.Parameters.Add(p);

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