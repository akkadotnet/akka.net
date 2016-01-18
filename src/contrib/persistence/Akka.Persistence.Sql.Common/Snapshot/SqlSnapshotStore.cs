//-----------------------------------------------------------------------
// <copyright file="SqlSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Snapshot;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Abstract snapshot store implementation, customized to work with SQL-based persistence providers.
    /// </summary>
    public abstract class SqlSnapshotStore : SnapshotStore
    {
        /// <summary>
        /// List of cancellation tokens for all pending asynchronous database operations.
        /// </summary>
        private readonly CancellationTokenSource _pendingRequestsCancellation;

        protected SqlSnapshotStore()
        {
            QueryMapper = new DefaultSnapshotQueryMapper(Context.System.Serialization);
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// Returns a new instance of database connection.
        /// </summary>
        protected abstract DbConnection CreateDbConnection(string connectionString);

        /// <summary>
        /// Returns a new instance of database connection.
        /// </summary>
        public DbConnection CreateDbConnection()
        {
            return CreateDbConnection(GetConnectionString());
        }

        /// <summary>
        /// Gets settings for the current snapshot store.
        /// </summary>
        protected abstract SnapshotStoreSettings Settings { get; }

        /// <summary>
        /// Query builder used to convert snapshot store related operations into corresponding SQL queries.
        /// </summary>
        public ISnapshotQueryBuilder QueryBuilder { get; set; }

        /// <summary>
        /// Query mapper used to map SQL query results into snapshots.
        /// </summary>
        public ISnapshotQueryMapper QueryMapper { get; set; }

        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            _pendingRequestsCancellation.Cancel();
        }

        protected virtual string GetConnectionString()
        {
            var connectionString = Settings.ConnectionString;
            return string.IsNullOrEmpty(connectionString)
                ? ConfigurationManager.ConnectionStrings[Settings.ConnectionStringName].ConnectionString
                : connectionString;
        }

        /// <summary>
        /// Asynchronously loads snapshot with the highest sequence number for a persistent actor/view matching specified criteria.
        /// </summary>
        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var sqlCommand = QueryBuilder.SelectSnapshot(persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
                CompleteCommand(sqlCommand, connection);
                
                var reader = await sqlCommand.ExecuteReaderAsync(_pendingRequestsCancellation.Token);
                try
                {
                    return reader.Read() ? QueryMapper.Map(reader) : null;
                }
                finally
                {
                    reader.Close();
                }
            }
        }

        /// <summary>
        /// Asynchronously stores a snapshot with metadata as record in SQL table.
        /// </summary>
        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();

                var entry = ToSnapshotEntry(metadata, snapshot);
                var sqlCommand = QueryBuilder.InsertSnapshot(entry);
                CompleteCommand(sqlCommand, connection);
                
                await sqlCommand.ExecuteNonQueryAsync(_pendingRequestsCancellation.Token);
            }
        }

        protected override void Saved(SnapshotMetadata metadata) { }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                var sqlCommand = QueryBuilder.DeleteOne(metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp);
                CompleteCommand(sqlCommand, connection);

                await sqlCommand.ExecuteNonQueryAsync();
            }
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                var sqlCommand = QueryBuilder.DeleteMany(persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
                CompleteCommand(sqlCommand, connection);

                await sqlCommand.ExecuteNonQueryAsync();
            }
        }

        private void CompleteCommand(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;
            command.CommandTimeout = (int)Settings.ConnectionTimeout.TotalMilliseconds;
        }

        private SnapshotEntry ToSnapshotEntry(SnapshotMetadata metadata, object snapshot)
        {
            var snapshotType = snapshot.GetType();
            var serializer = Context.System.Serialization.FindSerializerForType(snapshotType);

            var binary = serializer.ToBinary(snapshot);

            return new SnapshotEntry(
                persistenceId: metadata.PersistenceId,
                sequenceNr: metadata.SequenceNr,
                timestamp: metadata.Timestamp,
                snapshotType: snapshotType.QualifiedTypeName(),
                snapshot: binary);
        }
    }
}