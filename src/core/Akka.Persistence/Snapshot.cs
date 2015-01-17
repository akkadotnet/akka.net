using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Akka.Persistence
{
    public sealed class SnapshotMetadata : IEquatable<SnapshotMetadata>
    {
        internal class SnapshotMetadataTimestampComparer : IComparer<SnapshotMetadata>
        {
            public int Compare(SnapshotMetadata x, SnapshotMetadata y)
            {
                return x.Timestamp.CompareTo(y.Timestamp);
            }
        }

        public static readonly IComparer<SnapshotMetadata> TimestampComparer = new SnapshotMetadataTimestampComparer();

        public SnapshotMetadata(string persistenceId, long sequenceNr)
            : this(persistenceId, sequenceNr, DateTime.MinValue)
        {
        }

        [JsonConstructor]
        public SnapshotMetadata(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
        }

        /// <summary>
        /// Id of the persistent actor, from which the snapshot was taken.
        /// </summary>
        public string PersistenceId { get; private set; }

        /// <summary>
        /// Sequence number at which a snapshot was taken.
        /// </summary>
        public long SequenceNr { get; private set; }

        /// <summary>
        /// Time at which the snapshot was saved.
        /// </summary>
        public DateTime Timestamp { get; private set; }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is SnapshotMetadata && Equals((SnapshotMetadata)obj);
        }

        public bool Equals(SnapshotMetadata other)
        {
            return string.Equals(PersistenceId, other.PersistenceId) && SequenceNr == other.SequenceNr && Timestamp.Equals(other.Timestamp);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful saving of a snapshot.
    /// </summary>
    public sealed class SaveSnapshotSuccess
    {
        public SaveSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        public SnapshotMetadata Metadata { get; private set; }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed saving a snapshot.
    /// </summary>
    public sealed class SaveSnapshotFailure
    {
        public SaveSnapshotFailure(SnapshotMetadata metadata, Exception cause)
        {
            Metadata = metadata;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; private set; }

        /// <summary>
        /// A failure cause.
        /// </summary>
        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Offers a <see cref="PersistentActor"/> a previously saved snapshot during recovery.
    /// This offer is received before any further replayed messages.
    /// </summary>
    public sealed class SnapshotOffer
    {
        public SnapshotOffer(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }

    /// <summary>
    /// Selection criteria for loading and deleting a snapshots.
    /// </summary>
    public sealed class SnapshotSelectionCriteria
    {
        public static readonly SnapshotSelectionCriteria Latest = new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue);
        public static readonly SnapshotSelectionCriteria None = new SnapshotSelectionCriteria(0L, DateTime.MinValue);

        [JsonConstructor]
        public SnapshotSelectionCriteria(long maxSequenceNr, DateTime maxTimeStamp)
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
        }

        public SnapshotSelectionCriteria(long maxSequenceNr) : this(maxSequenceNr, DateTime.MaxValue)
        {
        }

        /// <summary>
        /// Upper bound for a selected snapshot's sequence number.
        /// </summary>
        public long MaxSequenceNr { get; private set; }

        /// <summary>
        /// Upper bound for a selected snapshot's timestamp.
        /// </summary>
        public DateTime MaxTimeStamp { get; private set; }

        internal SnapshotSelectionCriteria Limit(long toSequenceNr)
        {
            return toSequenceNr < MaxSequenceNr
                ? new SnapshotSelectionCriteria(toSequenceNr, MaxTimeStamp)
                : this;
        }

        internal bool IsMatch(SnapshotMetadata metadata)
        {
            return metadata.SequenceNr <= MaxSequenceNr && metadata.Timestamp <= MaxTimeStamp;
        }
    }

    /// <summary>
    /// A selected snapshot matching <see cref="SnapshotSelectionCriteria"/>.
    /// </summary>
    public sealed class SelectedSnapshot
    {
        public SelectedSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }


    #region Internal API for Snapshot protocol

    /// <summary>
    /// Insealed classs a snapshot store to load the snapshot.
    /// </summary>
    public sealed class LoadSnapshot
    {
        public LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Persitent actor identifier.
        /// </summary>
        public string PersistenceId { get; private set; }

        /// <summary>
        /// Criteria for selecting snapshot, from which the recovery should start.
        /// </summary>
        public SnapshotSelectionCriteria Criteria { get; private set; }

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery.
        /// </summary>
        public long ToSequenceNr { get; private set; }
    }

    /// <summary>
    /// Response to a <see cref="LoadSnapshot"/> message.
    /// </summary>
    public sealed class LoadSnapshotResult
    {
        public LoadSnapshotResult(SelectedSnapshot snapshot, long toSequenceNr)
        {
            Snapshot = snapshot;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Loaded snapshot or null if none provided.
        /// </summary>
        public SelectedSnapshot Snapshot { get; private set; }
        public long ToSequenceNr { get; private set; }
    }

    /// <summary>
    /// Insealed classs snapshot store to save a snapshot.
    /// </summary>
    public sealed class SaveSnapshot
    {
        public SaveSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }

    /// <summary>
    /// Insealed classs a snapshot store to save a snapshot.
    /// </summary>
    public sealed class DeleteSnapshot
    {
        public DeleteSnapshot(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        public SnapshotMetadata Metadata { get; private set; }
    }

    /// <summary>
    /// Insealed classs a snapshot store to delete all snapshots that match provided criteria.
    /// </summary>
    public sealed class DeleteSnapshots
    {
        public DeleteSnapshots(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
        }

        public string PersistenceId { get; private set; }
        public SnapshotSelectionCriteria Criteria { get; private set; }
    }

    #endregion
}