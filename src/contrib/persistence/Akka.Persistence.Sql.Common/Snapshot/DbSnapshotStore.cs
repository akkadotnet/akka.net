//-----------------------------------------------------------------------
// <copyright file="DbSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Snapshot;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Abstract snapshot store implementation, customized to work with SQL-based persistence providers.
    /// </summary>
    /*TODO: this class is not used*/public abstract class DbSnapshotStore : SnapshotStore
    {
        /// <summary>
        /// List of cancellation tokens for all pending asynchronous database operations.
        /// </summary>
        protected readonly LinkedList<CancellationTokenSource> PendingOperations;

        private DbConnection _connection;

        protected DbSnapshotStore()
        {
            QueryMapper = new DefaultSnapshotQueryMapper(Context.System.Serialization);
            PendingOperations = new LinkedList<CancellationTokenSource>();
        }

        /// <summary>
        /// Returns a new instance of database connection.
        /// </summary>
        protected abstract DbConnection CreateDbConnection();

        /// <summary>
        /// Gets settings for the current snapshot store.
        /// </summary>
        protected abstract SnapshotStoreSettings Settings { get; }

        /// <summary>
        /// Gets current database connection.
        /// </summary>
        public DbConnection DbConnection { get { return _connection; } }

        /// <summary>
        /// Query builder used to convert snapshot store related operations into corresponding SQL queries.
        /// </summary>
        public ISnapshotQueryBuilder QueryBuilder { get; set; }

        /// <summary>
        /// Query mapper used to map SQL query results into snapshots.
        /// </summary>
        public ISnapshotQueryMapper QueryMapper { get; set; }

        protected override void PreStart()
        {
            base.PreStart();

            _connection = CreateDbConnection();
            _connection.Open();
        }

        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            var node = PendingOperations.First;
            while (node != null)
            {
                var curr = node;
                node = node.Next;

                curr.Value.Cancel();
                PendingOperations.Remove(curr);
            }

            _connection.Close();
        }

        /// <summary>
        /// Asynchronously loads snapshot with the highest sequence number for a persistent actor/view matching specified criteria.
        /// </summary>
        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var sqlCommand = QueryBuilder.SelectSnapshot(persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
            CompleteCommand(sqlCommand);

            var tokenSource = GetCancellationTokenSource();
            return sqlCommand
                .ExecuteReaderAsync(tokenSource.Token)
                .ContinueWith(task =>
                {
                    var reader = task.Result;
                    try
                    {
                        return reader.Read() ? QueryMapper.Map(reader) : null;
                    }
                    finally
                    {
                        PendingOperations.Remove(tokenSource);
                        reader.Close();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        /// <summary>
        /// Asynchronously stores a snapshot with metadata as record in SQL table.
        /// </summary>
        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            var entry = ToSnapshotEntry(metadata, snapshot);
            var sqlCommand = QueryBuilder.InsertSnapshot(entry);
            CompleteCommand(sqlCommand);

            var tokenSource = GetCancellationTokenSource();

            return sqlCommand.ExecuteNonQueryAsync(tokenSource.Token)
                .ContinueWith(task =>
                {
                    PendingOperations.Remove(tokenSource);
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        protected override void Saved(SnapshotMetadata metadata) { }

        protected override void Delete(SnapshotMetadata metadata)
        {
            var sqlCommand = QueryBuilder.DeleteOne(metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp);
            CompleteCommand(sqlCommand);

            sqlCommand.ExecuteNonQuery();
        }

        protected override void Delete(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var sqlCommand = QueryBuilder.DeleteMany(persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
            CompleteCommand(sqlCommand);

            sqlCommand.ExecuteNonQuery();
        }

        private void CompleteCommand(DbCommand command)
        {
            command.Connection = _connection;
            command.CommandTimeout = (int)Settings.ConnectionTimeout.TotalMilliseconds;
        }

        private CancellationTokenSource GetCancellationTokenSource()
        {
            var source = new CancellationTokenSource();
            PendingOperations.AddLast(source);
            return source;
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