using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Streams;
using Akka.Util;
using LinqToDB;

namespace Akka.Persistence.Sql.Linq2Db.Snapshot
{
    public class ByteArraySnapshotDao : ISnapshotDao
    {
        private AkkaPersistenceDataConnectionFactory _connectionFactory;
        private IMaterializer _mat;
        private SnapshotConfig _snapshotConfig;
        private Akka.Serialization.Serialization _serialization;
        private ByteArraySnapshotSerializer _serializer;
        private ILoggingAdapter _logger;

        public void InitializeTables()
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                try
                {
                    conn.CreateTable<SnapshotRow>();
                }
                catch (Exception e)
                {
                    _logger.Warning(e,$"Could not Create Snapshot Table {_snapshotConfig.TableConfig.TableName} as requested by config.");
                }
            }
        }
        public ByteArraySnapshotDao(
            AkkaPersistenceDataConnectionFactory connectionFactory,
            SnapshotConfig snapshotConfig, Akka.Serialization.Serialization serialization,
            IMaterializer mat, ILoggingAdapter logger)
        {
            _logger = logger;
            _mat = mat;
            _snapshotConfig = snapshotConfig;
            _serialization = serialization;
            _connectionFactory = connectionFactory;
            _serializer = new ByteArraySnapshotSerializer(serialization, _snapshotConfig);
        }
        public async Task DeleteAllSnapshots(string persistenceId)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId)
                    .DeleteAsync();
            }
        }

        public async Task DeleteUpToMaxSequenceNr(string persistenceId, long maxSequenceNr)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                await conn.GetTable<SnapshotRow>()
                    .Where(r =>
                        r.PersistenceId == persistenceId &&
                        r.SequenceNumber <= maxSequenceNr).DeleteAsync();
            }
        }

        public async Task DeleteUpToMaxTimestamp(string persistenceId, DateTime maxTimestamp)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                await conn.GetTable<SnapshotRow>()
                    .Where(r =>
                        r.PersistenceId == persistenceId &&
                        r.Created <= maxTimestamp).DeleteAsync();
            }
        }

        public async Task DeleteUpToMaxSequenceNrAndMaxTimestamp(string persistenceId,
            long maxSequenceNr, DateTime maxTimestamp)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                await conn.GetTable<SnapshotRow>()
                    .Where(r =>
                        r.PersistenceId == persistenceId &&
                        r.SequenceNumber <= maxSequenceNr &&
                        r.Created <= maxTimestamp).DeleteAsync();
            }
        }

        public async Task<Option<SelectedSnapshot>> LatestSnapshot(string persistenceId)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                var row = await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId)
                    .OrderByDescending(t => t.SequenceNumber)
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    return _serializer.Deserialize(row).Get();
                }
                return Option<SelectedSnapshot>.None;
            }
        }

        public async Task<Option<SelectedSnapshot>> SnapshotForMaxTimestamp(string persistenceId, DateTime timestamp)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                var row = await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId && r.Created <= timestamp)
                    .OrderByDescending(t => t.SequenceNumber)
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    return _serializer.Deserialize(row).Get();
                }
                return Option<SelectedSnapshot>.None;
            }
        }

        public async Task<Option<SelectedSnapshot>> SnapshotForMaxSequenceNr(string persistenceId, long sequenceNr)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                var row = await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId && r.SequenceNumber <= sequenceNr)
                    .OrderByDescending(t => t.SequenceNumber)
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    return _serializer.Deserialize(row).Get();
                }
                return Option<SelectedSnapshot>.None;
            }
        }

        public async Task<Option<SelectedSnapshot>> SnapshotForMaxSequenceNrAndMaxTimestamp(string persistenceId,
            long sequenceNr, DateTime timestamp)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                var row = await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId && r.SequenceNumber <= sequenceNr && r.Created <= timestamp)
                    .OrderByDescending(t => t.SequenceNumber)
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    return _serializer.Deserialize(row).Get();
                }
                return Option<SelectedSnapshot>.None;
            }
        }

        public async Task Delete(string persistenceId, long sequenceNr)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                var row = await conn.GetTable<SnapshotRow>()
                    .Where(r => r.PersistenceId == persistenceId && r.SequenceNumber == sequenceNr)
                    .DeleteAsync();
            }
        }

        public async Task Save(SnapshotMetadata snapshotMetadata, object snapshot)
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                await conn.InsertOrReplaceAsync(_serializer.Serialize(snapshotMetadata, snapshot).Get());
            }
        }
    }
}