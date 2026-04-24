//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Util.Internal;

namespace Akka.Persistence.Snapshot
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// In-memory SnapshotStore implementation.
    ///
    /// Uses a channel-based drain-on-read pattern with immutable collections to handle
    /// the concurrent access pattern imposed by SnapshotStore. Writes and deletes enqueue to
    /// an unbounded channel (never blocking), and reads drain the channel first to ensure
    /// all pending operations are visible before returning results.
    /// </summary>
    public class MemorySnapshotStore : SnapshotStore
    {
        /// <summary>
        /// Represents an operation to be applied to the snapshot store.
        /// </summary>
        internal interface ISnapshotOperation { }

        /// <summary>
        /// Write a snapshot.
        /// </summary>
        internal sealed class WriteSnapshot : ISnapshotOperation
        {
            public SnapshotEntry Entry { get; }
            public WriteSnapshot(SnapshotEntry entry) => Entry = entry;
        }

        /// <summary>
        /// Delete a specific snapshot by metadata.
        /// </summary>
        internal sealed class DeleteSnapshot : ISnapshotOperation
        {
            public string PersistenceId { get; }
            public long SequenceNr { get; }
            public long Timestamp { get; }

            public DeleteSnapshot(SnapshotMetadata metadata)
            {
                PersistenceId = metadata.PersistenceId;
                SequenceNr = metadata.SequenceNr;
                Timestamp = metadata.Timestamp.Ticks;
            }
        }

        /// <summary>
        /// Delete snapshots matching criteria.
        /// </summary>
        internal sealed class DeleteSnapshotRange : ISnapshotOperation
        {
            public string PersistenceId { get; }
            public SnapshotSelectionCriteria Criteria { get; }

            public DeleteSnapshotRange(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                PersistenceId = persistenceId;
                Criteria = criteria;
            }
        }

        /// <summary>
        /// Storage container for snapshot data. Encapsulates the pending operations channel
        /// and immutable snapshot state.
        /// </summary>
        protected sealed class SnapshotStorage
        {
            /// <summary>
            /// Lock for serializing drain operations. Reads can happen concurrently
            /// from multiple thread pool threads due to SnapshotStore's async pattern.
            /// </summary>
            internal readonly object DrainLock = new();

            /// <summary>
            /// Pending operations channel — unbounded, writers never block.
            /// </summary>
            internal readonly Channel<ISnapshotOperation> PendingOperations =
                Channel.CreateUnbounded<ISnapshotOperation>(
                    new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

            /// <summary>
            /// All snapshots stored in memory.
            /// </summary>
            internal ImmutableList<SnapshotEntry> Snapshots = ImmutableList<SnapshotEntry>.Empty;
        }

        private readonly SnapshotStorage _storage = new();

        /// <summary>
        /// Storage property for accessing snapshot data. Override in subclasses to share storage.
        /// </summary>
        protected virtual SnapshotStorage Storage => _storage;

        /// <summary>
        /// Drains all pending operations from the channel into the immutable snapshot state.
        /// Must be called before any read operation to ensure all operations are visible.
        /// </summary>
        private void DrainPendingOperations()
        {
            lock (Storage.DrainLock)
            {
                while (Storage.PendingOperations.Reader.TryRead(out var op))
                {
                    switch (op)
                    {
                        case WriteSnapshot write:
                            var item = write.Entry;
                            var existingIndex = Storage.Snapshots.FindIndex(x => x.Id == item.Id);
                            if (existingIndex >= 0)
                                Storage.Snapshots = Storage.Snapshots.SetItem(existingIndex, item);
                            else
                                Storage.Snapshots = Storage.Snapshots.Add(item);
                            break;

                        case DeleteSnapshot del:
                            var snapshot = Storage.Snapshots.FirstOrDefault(x =>
                                x.PersistenceId == del.PersistenceId
                                && (del.SequenceNr <= 0 || del.SequenceNr == long.MaxValue || x.SequenceNr == del.SequenceNr)
                                && (del.Timestamp == DateTime.MinValue.Ticks || del.Timestamp == DateTime.MaxValue.Ticks || x.Timestamp == del.Timestamp));
                            if (snapshot != null)
                                Storage.Snapshots = Storage.Snapshots.Remove(snapshot);
                            break;

                        case DeleteSnapshotRange range:
                            var filter = CreateRangeFilter(range.PersistenceId, range.Criteria);
                            Storage.Snapshots = Storage.Snapshots.RemoveAll(x => filter(x));
                            break;
                    }
                }
            }
        }

        protected override Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
        {
            // Queue delete operation - will be processed during next drain
            Storage.PendingOperations.Writer.TryWrite(new DeleteSnapshot(metadata));
            return TaskEx.Completed;
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            // Queue delete operation - will be processed during next drain
            Storage.PendingOperations.Writer.TryWrite(new DeleteSnapshotRange(persistenceId, criteria));
            return TaskEx.Completed;
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            DrainPendingOperations();

            var filter = CreateRangeFilter(persistenceId, criteria);
            var snapshot = Storage.Snapshots
                .Where(filter)
                .OrderByDescending(x => x.SequenceNr)
                .Take(1)
                .Select(x => ToSelectedSnapshot(x))
                .FirstOrDefault();

            return Task.FromResult(snapshot);
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            var snapshotEntry = ToSnapshotEntry(metadata, snapshot);

            // Non-blocking write to channel — TryWrite always succeeds on unbounded channel
            Storage.PendingOperations.Writer.TryWrite(new WriteSnapshot(snapshotEntry));

            return TaskEx.Completed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Func<SnapshotEntry, bool> CreateRangeFilter(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            return (x => x.PersistenceId == persistenceId &&
            (criteria.MaxSequenceNr <= 0 || criteria.MaxSequenceNr == long.MaxValue || x.SequenceNr <= criteria.MaxSequenceNr) &&
            (criteria.MaxTimeStamp == DateTime.MinValue || criteria.MaxTimeStamp == DateTime.MaxValue || x.Timestamp <= criteria.MaxTimeStamp.Ticks));
        }

        private static SnapshotEntry ToSnapshotEntry(SnapshotMetadata metadata, object snapshot)
        {
            return new SnapshotEntry(
                id: metadata.PersistenceId + "_" + metadata.SequenceNr,
                persistenceId: metadata.PersistenceId,
                sequenceNr: metadata.SequenceNr,
                timestamp: metadata.Timestamp.Ticks,
                snapshot: snapshot);
        }

        private static SelectedSnapshot ToSelectedSnapshot(SnapshotEntry entry)
        {
            return new SelectedSnapshot(metadata: new SnapshotMetadata(
                persistenceId: entry.PersistenceId,
                sequenceNr: entry.SequenceNr,
                timestamp: DateTime.SpecifyKind(new DateTime(entry.Timestamp), DateTimeKind.Utc)),
                snapshot: entry.Snapshot);
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Represents a snapshot stored inside the in-memory <see cref="SnapshotStore"/>.
    /// Immutable by design to support concurrent access patterns.
    /// </summary>
    public sealed class SnapshotEntry
    {
        public SnapshotEntry(string id, string persistenceId, long sequenceNr, long timestamp, object snapshot)
        {
            Id = id;
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
            Snapshot = snapshot;
        }

        public string Id { get; }
        public string PersistenceId { get; }
        public long SequenceNr { get; }
        public long Timestamp { get; }
        public object Snapshot { get; }
    }
}
