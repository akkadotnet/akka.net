using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Persistence.Snapshot;
using Akka.Serialization;
using Cassandra;

namespace Akka.Persistence.Cassandra.Snapshot
{
    /// <summary>
    /// A SnapshotStore implementation for writing snapshots to Cassandra.
    /// </summary>
    public class CassandraSnapshotStore : SnapshotStore
    {
        private static readonly Type SnapshotType = typeof (Serialization.Snapshot);

        private readonly CassandraExtension _cassandraExtension;
        private readonly Serializer _serializer;
        private readonly ILoggingAdapter _log;
        private readonly bool _publish;

        private ISession _session;
        private PreparedStatement _writeSnapshot;
        private PreparedStatement _deleteSnapshot;
        private PreparedStatement _selectSnapshot;
        private PreparedStatement _selectSnapshotMetadata;
        
        public CassandraSnapshotStore()
        {
            _cassandraExtension = CassandraPersistence.Instance.Apply(Context.System);
            _serializer = Context.System.Serialization.FindSerializerForType(SnapshotType);
            _log = Context.System.Log;

            // Here so we can emulate the base class behavior but do deletes async
            PersistenceExtension persistence = Context.System.PersistenceExtension();
            _publish = persistence.Settings.Internal.PublishPluginCommands;
        }

        protected override void PreStart()
        {
            base.PreStart();
            
            // Get a session to talk to Cassandra with
            CassandraSnapshotStoreSettings settings = _cassandraExtension.SnapshotStoreSettings;
            _session = _cassandraExtension.SessionManager.ResolveSession(settings.SessionKey);

            // Create the keyspace if necessary and always attempt to create the table
            if (settings.KeyspaceAutocreate)
                _session.Execute(string.Format(SnapshotStoreStatements.CreateKeyspace, settings.Keyspace, settings.KeyspaceCreationOptions));

            var fullyQualifiedTableName = string.Format("{0}.{1}", settings.Keyspace, settings.Table);
            var createTable = string.IsNullOrWhiteSpace(settings.TableCreationProperties)
                                  ? string.Format(SnapshotStoreStatements.CreateTable, fullyQualifiedTableName, string.Empty, string.Empty)
                                  : string.Format(SnapshotStoreStatements.CreateTable, fullyQualifiedTableName, " AND ",
                                                  settings.TableCreationProperties);

            _session.Execute(createTable);

            // Prepare some statements
            _writeSnapshot = _session.PrepareFormat(SnapshotStoreStatements.WriteSnapshot, fullyQualifiedTableName);
            _deleteSnapshot = _session.PrepareFormat(SnapshotStoreStatements.DeleteSnapshot, fullyQualifiedTableName);
            _selectSnapshot = _session.PrepareFormat(SnapshotStoreStatements.SelectSnapshot, fullyQualifiedTableName);
            _selectSnapshotMetadata = _session.PrepareFormat(SnapshotStoreStatements.SelectSnapshotMetadata, fullyQualifiedTableName);
        }

        protected override bool Receive(object message)
        {
            // Make deletes async as well, but make sure we still publish like the base class does
            if (message is DeleteSnapshot)
            {
                HandleDeleteAsync((DeleteSnapshot) message, msg => DeleteAsync(msg.Metadata));
            }
            else if (message is DeleteSnapshots)
            {
                HandleDeleteAsync((DeleteSnapshots) message, msg => DeleteAsync(msg.PersistenceId, msg.Criteria));
            }
            else
            {
                return base.Receive(message);
            }

            return true;
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            bool hasNextPage = true;
            byte[] nextPageState = null;

            while (hasNextPage)
            {
                // Get a page of metadata that match the criteria
                IStatement getMetadata = _selectSnapshotMetadata.Bind(persistenceId, criteria.MaxSequenceNr)
                                                                .SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.ReadConsistency)
                                                                .SetPageSize(_cassandraExtension.SnapshotStoreSettings.MaxMetadataResultSize)
                                                                .SetPagingState(nextPageState)
                                                                .SetAutoPage(false);
                RowSet metadataRows = await _session.ExecuteAsync(getMetadata).ConfigureAwait(false);

                nextPageState = metadataRows.PagingState;
                hasNextPage = nextPageState != null;
                IEnumerable<SnapshotMetadata> page = metadataRows.Select(MapRowToSnapshotMetadata)
                                                                 .Where(md => md.Timestamp <= criteria.MaxTimeStamp);

                // Try to get the first available snapshot from the page
                foreach (SnapshotMetadata md in page)
                {
                    try
                    {
                        IStatement getSnapshot = _selectSnapshot.Bind(md.PersistenceId, md.SequenceNr)
                                                                .SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.ReadConsistency);
                        RowSet snapshotRows = await _session.ExecuteAsync(getSnapshot).ConfigureAwait(false);

                        // If we didn't get a snapshot for some reason, just try the next one
                        Row snapshotRow = snapshotRows.SingleOrDefault();
                        if (snapshotRow == null)
                            continue;

                        // We found a snapshot so create the necessary class and return the result
                        return new SelectedSnapshot(md, Deserialize(snapshotRow.GetValue<byte[]>("snapshot")));
                    }
                    catch (Exception e)
                    {
                        // If there is a problem, just try the next snapshot
                        _log.Warning("Unexpected exception while retrieveing snapshot {0} for id {1}: {2}", md.SequenceNr, md.PersistenceId, e);
                    }
                }

                // Just try the next page if available
            }

            // Out of snapshots that match or none found
            return null;
        }
        
        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            IStatement bound = _writeSnapshot.Bind(metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp.Ticks, Serialize(snapshot))
                                             .SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.WriteConsistency);
            return _session.ExecuteAsync(bound);
        }

        protected Task DeleteAsync(SnapshotMetadata metadata)
        {
            IStatement bound = _deleteSnapshot.Bind(metadata.PersistenceId, metadata.SequenceNr)
                                              .SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.WriteConsistency);
            return _session.ExecuteAsync(bound);
        }

        protected async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // Use a batch to delete all matching snapshots
            var batch = new BatchStatement();

            bool hasNextPage = true;
            byte[] nextPageState = null;

            while (hasNextPage)
            {
                // Get a page of metadata that match the criteria
                IStatement getMetadata = _selectSnapshotMetadata.Bind(persistenceId, criteria.MaxSequenceNr)
                                                                .SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.ReadConsistency)
                                                                .SetPageSize(_cassandraExtension.SnapshotStoreSettings.MaxMetadataResultSize)
                                                                .SetPagingState(nextPageState)
                                                                .SetAutoPage(false);
                RowSet metadataRows = await _session.ExecuteAsync(getMetadata).ConfigureAwait(false);

                nextPageState = metadataRows.PagingState;
                hasNextPage = nextPageState != null;
                IEnumerable<SnapshotMetadata> page = metadataRows.Select(MapRowToSnapshotMetadata)
                                                                 .Where(md => md.Timestamp <= criteria.MaxTimeStamp);
                // Add any matching snapshots from the page to the batch
                foreach (SnapshotMetadata md in page)
                    batch.Add(_deleteSnapshot.Bind(md.PersistenceId, md.SequenceNr));

                // Go to next page if available
            }
            
            if (batch.IsEmpty)
                return;

            // Send the batch of deletes
            batch.SetConsistencyLevel(_cassandraExtension.SnapshotStoreSettings.WriteConsistency);
            await _session.ExecuteAsync(batch).ConfigureAwait(false);
        }

        protected override void Saved(SnapshotMetadata metadata)
        {
            // No op
        }

        protected override void Delete(SnapshotMetadata metadata)
        {
            // Should never get called
            throw new NotSupportedException("Deletes are handled async by this snapshot store.");
        }

        protected override void Delete(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // Should never get called
            throw new NotSupportedException("Deletes are handled async by this snapshot store.");
        }

        protected override void PostStop()
        {
            base.PostStop();

            if (_cassandraExtension != null && _session != null)
            {
                _cassandraExtension.SessionManager.ReleaseSession(_session);
                _session = null;
            }
        }

        private async Task HandleDeleteAsync<T>(T message, Func<T, Task> handler)
        {
            try
            {
                // Capture event stream so we can use it after await
                EventStream es = Context.System.EventStream;

                // Delete async, then publish if necessary
                await handler(message).ConfigureAwait(false);
                if (_publish)
                    es.Publish(message);
            }
            catch (Exception e)
            {
                _log.Error(e, "Unexpected error while deleting snapshot(s).");
            }
        }

        private object Deserialize(byte[] bytes)
        {
            return ((Serialization.Snapshot) _serializer.FromBinary(bytes, SnapshotType)).Data;
        }

        private byte[] Serialize(object snapshotData)
        {
            return _serializer.ToBinary(new Serialization.Snapshot(snapshotData));
        }

        private static SnapshotMetadata MapRowToSnapshotMetadata(Row row)
        {
            return new SnapshotMetadata(row.GetValue<string>("persistence_id"), row.GetValue<long>("sequence_number"),
                                        new DateTime(row.GetValue<long>("timestamp_ticks")));
        }
    }
}
