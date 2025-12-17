//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Util.Internal;

namespace Akka.Persistence.Snapshot
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// In-memory SnapshotStore implementation.
    /// </summary>
    public class MemorySnapshotStore : SnapshotStore
    {
        /// <summary>
        /// Lock for thread-safe access to the Snapshots collection.
        ///
        /// Note: We use locks instead of thread-safe collections (e.g., ConcurrentDictionary) because:
        /// 1. Each persistence ID can have multiple snapshots at different sequence numbers, requiring range queries
        /// 2. LoadAsync needs to find the highest sequenceNr matching criteria via enumeration and sorting
        /// 3. SaveAsync requires atomic check-then-update-or-add operations (FirstOrDefault + mutation/Add)
        /// 4. ConcurrentDictionary keyed by persistenceId would still require a non-thread-safe List/Bag per value
        ///
        /// The lock ensures atomicity of compound operations and consistent enumeration during LINQ queries.
        /// </summary>
        private readonly object _snapshotsLock = new();

        /// <summary>
        /// This is available to expose/override the snapshots in derived snapshot stores
        /// </summary>
        protected virtual List<SnapshotEntry> Snapshots { get; } = new();

        protected override Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
        {
            bool Pred(SnapshotEntry x) => x.PersistenceId == metadata.PersistenceId && (metadata.SequenceNr <= 0 || metadata.SequenceNr == long.MaxValue || x.SequenceNr == metadata.SequenceNr)
                                                                                    && (metadata.Timestamp == DateTime.MinValue || metadata.Timestamp == DateTime.MaxValue || x.Timestamp == metadata.Timestamp.Ticks);

            lock (_snapshotsLock)
            {
                var snapshot = Snapshots.FirstOrDefault(Pred);
                Snapshots.Remove(snapshot);
            }

            return TaskEx.Completed;
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            var filter = CreateRangeFilter(persistenceId, criteria);

            lock (_snapshotsLock)
            {
                Snapshots.RemoveAll(x => filter(x));
            }
            return TaskEx.Completed;
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            var filter = CreateRangeFilter(persistenceId, criteria);

            SelectedSnapshot snapshot;
            lock (_snapshotsLock)
            {
                snapshot = Snapshots.Where(filter).OrderByDescending(x => x.SequenceNr).Take(1).Select(x => ToSelectedSnapshot(x)).FirstOrDefault();
            }
            return Task.FromResult(snapshot);
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            var snapshotEntry = ToSnapshotEntry(metadata, snapshot);

            lock (_snapshotsLock)
            {
                var existingSnapshot = Snapshots.FirstOrDefault(CreateSnapshotIdFilter(snapshotEntry.Id));

                if (existingSnapshot != null)
                {
                    existingSnapshot.Snapshot = snapshotEntry.Snapshot;
                    existingSnapshot.Timestamp = snapshotEntry.Timestamp;
                }
                else
                {
                    Snapshots.Add(snapshotEntry);
                }
            }

            return TaskEx.Completed;
        }

        private static Func<SnapshotEntry, bool> CreateSnapshotIdFilter(string snapshotId)
        {
            return x => x.Id == snapshotId;
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
            return new SnapshotEntry
            {
                Id = metadata.PersistenceId + "_" + metadata.SequenceNr,
                PersistenceId = metadata.PersistenceId,
                SequenceNr = metadata.SequenceNr,
                Snapshot = snapshot,
                Timestamp = metadata.Timestamp.Ticks
            };
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
    /// Represents a snapshot stored inside the in-memory <see cref="SnapshotStore"/>
    /// </summary>
    public class SnapshotEntry
    {
        public string Id { get; set; }

        public string PersistenceId { get; set; }

        public long SequenceNr { get; set; }

        public long Timestamp { get; set; }

        public object Snapshot { get; set; }

    }
}
