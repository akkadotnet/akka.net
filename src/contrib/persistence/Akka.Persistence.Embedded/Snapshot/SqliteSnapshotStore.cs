//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Snapshot;

namespace Akka.Persistence.Embedded.Snapshot
{
    /// <summary>
    /// SQLite snapshot store. Uses the Akka.Persistence.Sql snapshot table. One worker thread with one
    /// connection runs every operation in arrival order.
    /// </summary>
    public sealed class SqliteSnapshotStore : SnapshotStore, IWithUnboundedStash
    {
        private readonly SnapshotSettings _settings;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly ExtendedActorSystem _system;
        private readonly SnapshotSql _sql;
        private readonly SqliteWorkerPool _worker;

        /// <summary>Plugin API: the stash. Set by the actor system.</summary>
        public IStash Stash { get; set; } = null!;

        /// <summary>Creates the snapshot store. Called by Akka.Persistence with the plugin's config section.</summary>
        public SqliteSnapshotStore(Config snapshotConfig)
        {
            var pluginPath = Self.Path.Name;
            _system = (ExtendedActorSystem)Context.System;
            _settings = SnapshotSettings.Create(snapshotConfig, pluginPath);

            _sql = new SnapshotSql(_settings);
            _worker = new SqliteWorkerPool(_settings.ConnectionString, 1, $"{pluginPath}-worker", _log);
        }

        protected override void PreStart()
        {
            base.PreStart();
            _worker.Start();

            var self = Self;
            _worker
                .Run(connection =>
                {
                    SqliteSchema.EnsureSnapshotSchema(connection, _settings);
                    return true;
                }, CancellationToken.None)
                .ContinueWith(
                    t => t.IsFaulted
                        ? (object)new Status.Failure(t.Exception!.GetBaseException())
                        : Status.Success.Instance,
                    TaskContinuationOptions.ExecuteSynchronously)
                .PipeTo(self);

            BecomeStacked(Initializing);
        }

        protected override void PostStop()
        {
            _worker.Dispose();
            base.PostStop();
        }

        private bool Initializing(object message)
        {
            switch (message)
            {
                case Status.Success:
                    UnbecomeStacked();
                    Stash.UnstashAll();
                    return true;

                case Status.Failure failure:
                    _log.Error(failure.Cause, "Failure during {0} initialization.", Self);
                    throw new ApplicationException("Failed to initialize SQLite snapshot store.", failure.Cause);

                default:
                    Stash.Stash();
                    return true;
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (message is EnsureInitialized)
            {
                Sender.Tell(Initialized.Instance);
                return true;
            }

            return false;
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            var row = await _worker
                .Run(connection => _sql.Load(connection, persistenceId, criteria), cancellationToken)
                .ConfigureAwait(false);
            if (row is null)
                return null!;

            if (row.SerializerId is null)
            {
                throw new SerializationException(
                    $"Snapshot row ({row.PersistenceId}, {row.SequenceNr}) has a NULL serializer_id. " +
                    "Akka.Persistence.Embedded cannot read rows without a serializer id (that needs Type.GetType). " +
                    "Re-write these rows with Akka.Persistence.Sql first.");
            }

            var snapshot = _system.Serialization.Deserialize(row.Payload, (int)row.SerializerId.Value, row.Manifest ?? string.Empty);
            var metadata = new SnapshotMetadata(row.PersistenceId, row.SequenceNr, new DateTime(row.Created, DateTimeKind.Utc));
            return new SelectedSnapshot(metadata, snapshot);
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            // serialize on the actor thread, then hand plain values to the worker. A serializer error faults the task
            // (-> SaveSnapshotFailure) instead of throwing into the caller.
            byte[] bytes;
            string manifest;
            int identifier;
            try
            {
                (bytes, manifest, identifier) = RowCodec.SerializePayload(_system, snapshot, _settings.Serializer);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }

            var created = metadata.Timestamp.Ticks;
            return _worker.Run(
                connection =>
                {
                    _sql.Save(connection, metadata.PersistenceId, metadata.SequenceNr, created, bytes, manifest, identifier);
                    return true;
                },
                cancellationToken);
        }

        protected override Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
            => _worker.Run(
                connection =>
                {
                    _sql.Delete(connection, metadata);
                    return true;
                },
                cancellationToken);

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
            => _worker.Run(
                connection =>
                {
                    _sql.Delete(connection, persistenceId, criteria);
                    return true;
                },
                cancellationToken);
    }
}
