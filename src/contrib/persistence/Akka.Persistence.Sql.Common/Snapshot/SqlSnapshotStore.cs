//-----------------------------------------------------------------------
// <copyright file="SqlSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Snapshot;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Abstract snapshot store implementation, customized to work with SQL-based persistence providers.
    /// </summary>
    public abstract class SqlSnapshotStore : SnapshotStore, IWithUnboundedStash
    {
        #region messages
        
        private sealed class Initialized
        {
            public static readonly Initialized Instance = new Initialized();
            private Initialized() { }
        }
            
        #endregion

        /// <summary>
        /// List of cancellation tokens for all pending asynchronous database operations.
        /// </summary>
        private readonly CancellationTokenSource _pendingRequestsCancellation;

        private readonly SnapshotStoreSettings _settings;

        protected SqlSnapshotStore(Config config)
        {
            _settings = new SnapshotStoreSettings(config);
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        private ILoggingAdapter _log;
        protected ILoggingAdapter Log => _log ?? (_log ?? Context.GetLogger());

        public IStash Stash { get; set; }

        /// <summary>
        /// Query executor used to convert snapshot store related operations into corresponding SQL queries.
        /// </summary>
        public abstract ISnapshotQueryExecutor QueryExecutor { get; }

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

        protected override void PreStart()
        {
            base.PreStart();
            if (_settings.AutoInitialize)
            {
                Initialize().PipeTo(Self);
                BecomeStacked(WaitingForInitialization);
            }
        }

        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            _pendingRequestsCancellation.Cancel();
        }

        private async Task<Initialized> Initialize()
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync(_pendingRequestsCancellation.Token);
                await QueryExecutor.CreateTableAsync(connection, _pendingRequestsCancellation.Token);
                return Initialized.Instance;
            }
        }

        private bool WaitingForInitialization(object message) => message.Match()
            .With<Initialized>(_ =>
            {
                UnbecomeStacked();
                Stash.UnstashAll();
            })
            .With<Failure>(failure =>
            {
                _log.Error(failure.Exception, "Error during snapshot store intiialization");
                Context.Stop(Self);
            })
            .Default(_ => Stash.Stash())
            .WasHandled;

        protected virtual string GetConnectionString()
        {
            var connectionString = _settings.ConnectionString;

#if CONFIGURATION
            if (string.IsNullOrEmpty(connectionString))
            {
                return System.Configuration.ConfigurationManager.ConnectionStrings[_settings.ConnectionStringName].ConnectionString;
            }
#endif

            return connectionString;
        }

        /// <summary>
        /// Asynchronously loads snapshot with the highest sequence number for a persistent actor/view matching specified criteria.
        /// </summary>
        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync(_pendingRequestsCancellation.Token);
                return await QueryExecutor.SelectSnapshotAsync(connection, _pendingRequestsCancellation.Token, persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
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
                await QueryExecutor.InsertAsync(connection, _pendingRequestsCancellation.Token, snapshot, metadata);
            }
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                DateTime? timestamp = metadata.Timestamp != DateTime.MinValue ? metadata.Timestamp : default(DateTime?);
                await QueryExecutor.DeleteAsync(connection, _pendingRequestsCancellation.Token, metadata.PersistenceId, metadata.SequenceNr, timestamp);
            }
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            {
                await connection.OpenAsync();
                await QueryExecutor.DeleteBatchAsync(connection, _pendingRequestsCancellation.Token, persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
            }
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
                manifest: snapshotType.QualifiedTypeName(),
                payload: binary);
        }
    }
}