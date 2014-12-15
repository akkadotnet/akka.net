using System;
using System.Collections.Generic;

namespace Akka.Persistence
{
    public struct SnapshotMetadata
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

        public SnapshotMetadata(string persistenceId, long sequenceNr, DateTime timestamp) : this()
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
        }

        public string PersistenceId { get; private set; }
        public long SequenceNr { get; private set; }
        public DateTime Timestamp { get; private set; }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is SnapshotMetadata && Equals((SnapshotMetadata) obj);
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

    public struct SaveSnapshotSuccess
    {
        public SaveSnapshotSuccess(SnapshotMetadata metadata) : this()
        {
            Metadata = metadata;
        }

        public SnapshotMetadata Metadata { get; private set; }
    }

    public struct SaveSnapshotFailure
    {
        public SaveSnapshotFailure(SnapshotMetadata metadata, Exception cause) : this()
        {
            Metadata = metadata;
            Cause = cause;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public Exception Cause { get; private set; }
    }

    public struct SnapshotOffer
    {
        public SnapshotOffer(SnapshotMetadata metadata, object snapshot) : this()
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }

    public struct SnapshotSelectionCriteria
    {
        public static readonly SnapshotSelectionCriteria Latest = new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue);
        public static readonly SnapshotSelectionCriteria None = new SnapshotSelectionCriteria(0L, DateTime.MinValue);

        public SnapshotSelectionCriteria(long maxSequenceNr, DateTime maxTimeStamp) 
            : this()
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
        }

        public long MaxSequenceNr { get; private set; }
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

    public struct SelectedSnapshot
    {
        public SelectedSnapshot(SnapshotMetadata metadata, object snapshot) : this()
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }

    public interface ISnapshotter
    {
        string SnapshoterId { get; }
        long SnapshotSequenceNr { get; }

        void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr);
        void SaveSnapshot(object snapshot);
        void DeleteSnapshot(long sequenceNr, long timestamp);
        void DeleteSnapshots(SnapshotSelectionCriteria criteria);
    }

    #region Internal API for Snapshot protocol

    internal struct LoadSnapshot
    {
        public LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr) : this()
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
            ToSequenceNr = toSequenceNr;
        }

        public string PersistenceId { get; private set; }
        public SnapshotSelectionCriteria Criteria { get; private set; }
        public long ToSequenceNr { get; private set; }
    }

    internal struct LoadSnapshotResult
    {
        public LoadSnapshotResult(SelectedSnapshot? snapshot, long toSequenceNr) : this()
        {
            Snapshot = snapshot;
            ToSequenceNr = toSequenceNr;
        }

        public SelectedSnapshot? Snapshot { get; private set; }
        public long ToSequenceNr { get; private set; }
    }

    internal struct SaveSnapshot
    {
        public SaveSnapshot(SnapshotMetadata metadata, object snapshot) : this()
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public SnapshotMetadata Metadata { get; private set; }
        public object Snapshot { get; private set; }
    }

    internal struct DeleteSnapshot
    {
        public DeleteSnapshot(SnapshotMetadata metadata) : this()
        {
            Metadata = metadata;
        }

        public SnapshotMetadata Metadata { get; private set; }
    }

    internal struct DeleteSnapshots
    {
        public DeleteSnapshots(string persistenceId, SnapshotSelectionCriteria criteria) : this()
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
        }

        public string PersistenceId { get; private set; }
        public SnapshotSelectionCriteria Criteria { get; private set; }
    }

    #endregion
}