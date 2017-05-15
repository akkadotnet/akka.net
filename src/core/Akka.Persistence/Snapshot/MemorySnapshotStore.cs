//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Akka.Persistence.Snapshot
{
    public class MemorySnapshotStore : SnapshotStore
    {
        private readonly List<SnapshotEntry> _snapshotCollection = new List<SnapshotEntry>();

        /// <summary>
        /// This is available to expose/override the snapshots in derived snapshot stores
        /// </summary>
        protected virtual List<SnapshotEntry> Snapshots { get { return _snapshotCollection; } }

        protected override Task DeleteAsync(SnapshotMetadata metadata)
        {
            Func<SnapshotEntry, bool> pred = x => x.PersistenceId == metadata.PersistenceId &&
            (metadata.SequenceNr <= 0 || metadata.SequenceNr == long.MaxValue || x.SequenceNr == metadata.SequenceNr) &&
            (metadata.Timestamp == DateTime.MinValue || metadata.Timestamp == DateTime.MaxValue || x.Timestamp == metadata.Timestamp.Ticks);

            return Task.Run(() =>
            {
                var snapshot = Snapshots.FirstOrDefault(pred);
                Snapshots.Remove(snapshot);
            });
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var filter = CreateRangeFilter(persistenceId, criteria);

            return Task.Run(() => { Snapshots.RemoveAll(x => filter(x)); });
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var filter = CreateRangeFilter(persistenceId, criteria);

            return Task.Run(() =>
            {
                var snapshot = Snapshots.Where(filter).OrderByDescending(x => x.SequenceNr).Take(1).Select(x => ToSelectedSnapshot(x)).FirstOrDefault();
                return snapshot;
            });
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            await Task.Run(() =>
            {
                var snapshotEntry = ToSnapshotEntry(metadata, snapshot);
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
            });
        }

        private Func<SnapshotEntry, bool> CreateSnapshotIdFilter(string snapshotId)
        {
            return x => x.Id == snapshotId;
        }

        private Func<SnapshotEntry, bool> CreateRangeFilter(string persistenceId, SnapshotSelectionCriteria criteria)
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
            return new SelectedSnapshot(new SnapshotMetadata(entry.PersistenceId, entry.SequenceNr, new DateTime(entry.Timestamp)), entry.Snapshot);
        }
    }

    public class SnapshotEntry
    {
        public string Id { get; set; }

        public string PersistenceId { get; set; }

        public long SequenceNr { get; set; }

        public long Timestamp { get; set; }

        public object Snapshot { get; set; }

    }
}
