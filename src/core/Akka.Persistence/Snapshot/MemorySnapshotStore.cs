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
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Persistence.Snapshot
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// In-memory SnapshotStore implementation.
    ///
    /// Uses a channel-based drain-on-read pattern with immutable collections to handle
    /// the concurrent access pattern imposed by SnapshotStore. All mutations (writes
    /// and deletes) enqueue to an unbounded channel (never blocking), and reads drain
    /// the channel first to ensure all pending operations are visible before returning results.
    /// </summary>
    public class MemorySnapshotStore : SnapshotStore
    {
        /// <summary>
        /// Represents a snapshot operation — either a write or a delete.
        /// </summary>
        internal readonly record struct SnapshotOperation(
            SnapshotEntry? Write = null,
            SnapshotMetadata? DeleteByMetadata = null,
            (string PersistenceId, SnapshotSelectionCriteria Criteria)? DeleteByCriteria = null);

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
            /// Tracks number of in-flight write/delete operations that have been dispatched
            /// but not yet completed. Reads must wait for this to reach zero.
            /// </summary>
            internal int InFlightOps;

            /// <summary>
            /// Event signaled when InFlightOps reaches zero.
            /// </summary>
            internal readonly ManualResetEventSlim InFlightComplete = new(true);

            /// <summary>
            /// Pending operations channel — unbounded, writers never block.
            /// </summary>
            internal readonly Channel<SnapshotOperation> PendingOps =
                Channel.CreateUnbounded<SnapshotOperation>(
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
        /// Tracks in-flight write/delete operations so reads can wait for pending ops to complete.
        /// This ensures operations dispatched via fire-and-forget (with Task.Yield) are visible
        /// before any read operations proceed.
        /// </summary>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            switch (message)
            {
                case SaveSnapshot:
                case DeleteSnapshot:
                case DeleteSnapshots:
                    // Increment in-flight counter before dispatch
                    if (Interlocked.Increment(ref Storage.InFlightOps) == 1)
                        Storage.InFlightComplete.Reset();
                    break;
            }

            return base.AroundReceive(receive, message);
        }

        /// <summary>
        /// Signals that an in-flight operation has completed.
        /// </summary>
        private void CompleteInFlightOp()
        {
            if (Interlocked.Decrement(ref Storage.InFlightOps) == 0)
                Storage.InFlightComplete.Set();
        }

        /// <summary>
        /// Drains all pending operations from the channel into the immutable snapshot state.
        /// Must be called before any read operation to ensure all mutations are visible.
        /// </summary>
        private void DrainPendingOps()
        {
            // Wait for any in-flight write/delete operations to complete first
            Storage.InFlightComplete.Wait();

            lock (Storage.DrainLock)
            {
                while (Storage.PendingOps.Reader.TryRead(out var op))
                {
                    if (op.Write is { } item)
                    {
                        var existingIndex = Storage.Snapshots.FindIndex(x => x.Id == item.Id);

                        if (existingIndex >= 0)
                        {
                            Storage.Snapshots = Storage.Snapshots.SetItem(existingIndex, item);
                        }
                        else
                        {
                            Storage.Snapshots = Storage.Snapshots.Add(item);
                        }
                    }
                    else if (op.DeleteByMetadata is { } metadata)
                    {
                        bool Pred(SnapshotEntry x) => x.PersistenceId == metadata.PersistenceId
                            && (metadata.SequenceNr <= 0 || metadata.SequenceNr == long.MaxValue || x.SequenceNr == metadata.SequenceNr)
                            && (metadata.Timestamp == DateTime.MinValue || metadata.Timestamp == DateTime.MaxValue || x.Timestamp == metadata.Timestamp.Ticks);

                        var snapshot = Storage.Snapshots.FirstOrDefault(Pred);
                        if (snapshot != null)
                        {
                            Storage.Snapshots = Storage.Snapshots.Remove(snapshot);
                        }
                    }
                    else if (op.DeleteByCriteria is { } deleteCriteria)
                    {
                        var filter = CreateRangeFilter(deleteCriteria.PersistenceId, deleteCriteria.Criteria);
                        Storage.Snapshots = Storage.Snapshots.RemoveAll(x => filter(x));
                    }
                }
            }
        }

        protected override Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
        {
            try
            {
                Storage.PendingOps.Writer.TryWrite(new SnapshotOperation(DeleteByMetadata: metadata));
                return TaskEx.Completed;
            }
            finally
            {
                CompleteInFlightOp();
            }
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            try
            {
                Storage.PendingOps.Writer.TryWrite(new SnapshotOperation(DeleteByCriteria: (persistenceId, criteria)));
                return TaskEx.Completed;
            }
            finally
            {
                CompleteInFlightOp();
            }
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            DrainPendingOps();

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
            try
            {
                var snapshotEntry = ToSnapshotEntry(metadata, snapshot);
                Storage.PendingOps.Writer.TryWrite(new SnapshotOperation(Write: snapshotEntry));
                return TaskEx.Completed;
            }
            finally
            {
                CompleteInFlightOp();
            }
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
