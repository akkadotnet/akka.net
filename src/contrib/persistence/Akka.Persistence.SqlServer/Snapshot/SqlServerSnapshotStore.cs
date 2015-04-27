using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Snapshot;

namespace Akka.Persistence.SqlServer.Snapshot
{
    /// <summary>
    /// Actor used for storing incoming snapshots into persistent snapshot store backed by SQL Server database.
    /// </summary>
    public class SqlServerSnapshotStore : SnapshotStore
    {
        private readonly SqlServerPersistenceExtension _extension;
        private SqlConnection _connection;

        protected readonly LinkedList<CancellationTokenSource> PendingOperations;

        public SqlServerSnapshotStore()
        {
            _extension = SqlServerPersistence.Instance.Apply(Context.System);

            var settings = _extension.SnapshotStoreSettings;
            QueryBuilder = new DefaultSnapshotQueryBuilder(settings.SchemaName, settings.TableName);
            QueryMapper = new DefaultSnapshotQueryMapper(Context.System.Serialization);
            PendingOperations = new LinkedList<CancellationTokenSource>();
        }

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

            _connection = new SqlConnection(_extension.SnapshotStoreSettings.ConnectionString);
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

        private void CompleteCommand(SqlCommand command)
        {
            command.Connection = _connection;
            command.CommandTimeout = (int)_extension.SnapshotStoreSettings.ConnectionTimeout.TotalMilliseconds;
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