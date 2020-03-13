//-----------------------------------------------------------------------
// <copyright file="SqlSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Util;
using Akka.Util.Internal;

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

        private readonly ExtendedActorSystem _actorSystem;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlSnapshotStore"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the snapshot store.</param>
        protected SqlSnapshotStore(Config config)
        {
            _actorSystem = Context.System.AsInstanceOf<ExtendedActorSystem>();
            _settings = new SnapshotStoreSettings(config);
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log => _log ?? (_log ?? Context.GetLogger());
        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash { get; set; }

        /// <summary>
        /// Query executor used to convert snapshot store related operations into corresponding SQL queries.
        /// </summary>
        public abstract ISnapshotQueryExecutor QueryExecutor { get; }

        /// <summary>
        /// Returns a new instance of database connection.
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected abstract DbConnection CreateDbConnection(string connectionString);

        /// <summary>
        /// Returns a new instance of database connection.
        /// </summary>
        /// <returns>TBD</returns>
        public DbConnection CreateDbConnection()
        {
            return CreateDbConnection(GetConnectionString());
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            base.PreStart();
            if (_settings.AutoInitialize)
            {
                Initialize().PipeTo(Self);
                BecomeStacked(WaitingForInitialization);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            _pendingRequestsCancellation.Cancel();
        }

        private async Task<object> Initialize()
        {
            try
            {
                using (var connection = CreateDbConnection())
                using (var nestedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    await connection.OpenAsync(nestedCancellationTokenSource.Token);
                    await QueryExecutor.CreateTableAsync(connection, nestedCancellationTokenSource.Token);
                    return Initialized.Instance;
                }
            }
            catch (Exception e)
            {
                return new Failure {Exception = e};
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
                Log.Error(failure.Exception, "Error during snapshot store initialization");
                Context.Stop(Self);
            })
            .Default(_ => Stash.Stash())
            .WasHandled;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected virtual string GetConnectionString()
        {
            var connectionString = _settings.ConnectionString;

#if CONFIGURATION
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[_settings.ConnectionStringName].ConnectionString;
            }
#endif

            return connectionString;
        }

        /// <summary>
        /// Asynchronously loads snapshot with the highest sequence number for a persistent actor/view matching specified criteria.
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <returns>TBD</returns>
        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            using (var nestedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(nestedCancellationTokenSource.Token);
                return await QueryExecutor.SelectSnapshotAsync(connection, nestedCancellationTokenSource.Token, persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
            }
        }

        /// <summary>
        /// Asynchronously stores a snapshot with metadata as record in SQL table.
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="snapshot">TBD</param>
        /// <returns>TBD</returns>
        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            using (var connection = CreateDbConnection())
            using (var nestedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(nestedCancellationTokenSource.Token);
                await QueryExecutor.InsertAsync(connection, nestedCancellationTokenSource.Token, snapshot, metadata);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <returns>TBD</returns>
        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            using (var connection = CreateDbConnection())
            using (var nestedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))    
            {
                await connection.OpenAsync(nestedCancellationTokenSource.Token);
                DateTime? timestamp = metadata.Timestamp != DateTime.MinValue ? metadata.Timestamp : default(DateTime?);
                await QueryExecutor.DeleteAsync(connection, nestedCancellationTokenSource.Token, metadata.PersistenceId, metadata.SequenceNr, timestamp);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <returns>TBD</returns>
        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = CreateDbConnection())
            using (var nestedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(nestedCancellationTokenSource.Token);
                await QueryExecutor.DeleteBatchAsync(connection, nestedCancellationTokenSource.Token, persistenceId, criteria.MaxSequenceNr, criteria.MaxTimeStamp);
            }
        }
        
        private SnapshotEntry ToSnapshotEntry(SnapshotMetadata metadata, object snapshot)
        {
            var snapshotType = snapshot.GetType();
            var serializer = Context.System.Serialization.FindSerializerForType(snapshotType, _settings.DefaultSerializer);

            var binary  = Akka.Serialization.Serialization.WithTransport(_actorSystem,
                () => serializer.ToBinary(snapshot));
            

            return new SnapshotEntry(
                persistenceId: metadata.PersistenceId,
                sequenceNr: metadata.SequenceNr,
                timestamp: metadata.Timestamp,
                manifest: snapshotType.TypeQualifiedName(),
                payload: binary);
        }
    }
}
