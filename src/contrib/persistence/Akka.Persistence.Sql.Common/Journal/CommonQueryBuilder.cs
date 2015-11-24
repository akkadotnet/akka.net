//-----------------------------------------------------------------------
// <copyright file="CommonQueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    internal class CommonQueryBuilder : IJournalQueryBuilder
    {
        private readonly string _tableName;

        private readonly string _selectHighestSequenceNrSql;
        private readonly string _insertMessagesSql;

        private readonly DbProviderFactory _dbFactory;

        public CommonQueryBuilder(DbProviderFactory dbFactory, string tableName)
        {
            _dbFactory = dbFactory;
            _tableName = tableName;
            _insertMessagesSql = string.Format(
                "INSERT INTO {0} (persistence_id, sequence_nr, is_deleted, manifest, timestamp, payload) VALUES (@PersistenceId, @SequenceNr, @IsDeleted, @Manifest, @Timestamp, @Payload)", _tableName);
            _selectHighestSequenceNrSql = string.Format(@"SELECT MAX(sequence_nr) FROM {0} WHERE persistence_id = @PersistenceId ", _tableName);
        }

        public DbCommand SelectEvents(IEnumerable<IHint> hints)
        {
            var sqlCommand = _dbFactory.CreateCommand();

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

        private string HintToSql(IHint hint, DbCommand command)
        {
            if (hint is TimestampRange)
            {
                var range = (TimestampRange)hint;
                var sb = new StringBuilder();
                
                if (range.From.HasValue)
                {
                    sb.Append(" timestamp >= @TimestampFrom ");
                    var p = command.CreateParameter();
                    p.Direction = ParameterDirection.Input;
                    p.ParameterName = "@TimestampFrom";
                    p.Value = range.From.Value;
                    command.Parameters.Add(p);
                }
                if (range.From.HasValue && range.To.HasValue) sb.Append("AND");
                if (range.To.HasValue)
                {
                    sb.Append(" timestamp < @TimestampTo ");
                    var p = command.CreateParameter();
                    p.Direction = ParameterDirection.Input;
                    p.ParameterName = "@TimestampTo";
                    p.Value = range.To.Value;
                    command.Parameters.Add(p);
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
                    var p = command.CreateParameter();
                    p.Direction = ParameterDirection.Input;
                    p.ParameterName = paramName;
                    p.Value = persistenceId;
                    command.Parameters.Add(p);
                }
                return range.PersistenceIds.Count == 0
                    ? string.Empty
                    : sb.Remove(sb.Length - 1, 1).Append(')').ToString();
            }
            else if (hint is WithManifest)
            {
                var manifest = (WithManifest)hint;
                var p = command.CreateParameter();
                p.Direction = ParameterDirection.Input;
                p.ParameterName = "@Manifest";
                p.Value = manifest.Manifest;
                command.Parameters.Add(p);
                return " manifest = @Manifest";
            }
            else throw new NotSupportedException(string.Format("Common journal doesn't support query with hint [{0}]", hint.GetType()));
        }

        public DbCommand SelectMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max)
        {
            var sb = new StringBuilder("SELECT");

            if (max != long.MaxValue)
            {
                //todo support for oracle and mysql
                //http://www.w3schools.com/sql/sql_top.asp
                sb.Append(" TOP ").Append(max);
            }
   
            sb.Append(@" persistence_id, sequence_nr, is_deleted, manifest, payload")
                .Append(" FROM ").Append(_tableName)
                .Append(" WHERE persistence_id = @PersistenceId ");

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

            var command = _dbFactory.CreateCommand();
            command.CommandText = sb.ToString();
            
            var p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            command.Parameters.Add(p);

            return command;
        }

        public DbCommand SelectHighestSequenceNr(string persistenceId)
        {
            var command = _dbFactory.CreateCommand();
            command.CommandText = _selectHighestSequenceNrSql;

            var p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            command.Parameters.Add(p);

            return command;
        }

        public DbCommand InsertBatchMessages(IPersistentRepresentation[] messages)
        {
            var command = _dbFactory.CreateCommand();
            command.CommandText = _insertMessagesSql;

            DbParameter p;
            
            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.DbType = DbType.String;
            //p.Value = persistenceId;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@SequenceNr";
            p.DbType = DbType.Int64;
            //p.Value = persistenceId;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@IsDeleted";
            p.DbType = DbType.Boolean;
            //p.Value = persistenceId;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Manifest";
            p.DbType = DbType.String;
            //p.Value = persistenceId;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Timestamp";
            p.DbType = DbType.DateTime;
            //p.Value = persistenceId;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@Payload";
            p.DbType = DbType.Binary;
            //p.Value = persistenceId;
            command.Parameters.Add(p);
            
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

            sb.Append(" WHERE persistence_id = @PersistenceId");

            if (toSequenceNr != long.MaxValue)
            {
                sb.Append(" AND sequence_nr <= ").Append(toSequenceNr);
            }

            var command = _dbFactory.CreateCommand();
            command.CommandText = sb.ToString();

            var p = command.CreateParameter();
            p.Direction = ParameterDirection.Input;
            p.ParameterName = "@PersistenceId";
            p.Value = persistenceId;
            command.Parameters.Add(p);

            return command;
        }
    }
}