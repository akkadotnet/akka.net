using System;

namespace Akka.Persistence
{
    public struct SnapshotMetadata
    {
        public SnapshotMetadata(string persistenceId, long sequenceNr, long timestamp = 0L) : this()
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
        }

        public string PersistenceId { get; private set; }
        public long SequenceNr { get; private set; }
        public long Timestamp { get; private set; }
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
        public static readonly SnapshotSelectionCriteria Latest = new SnapshotSelectionCriteria(long.MaxValue, long.MaxValue);
        public static readonly SnapshotSelectionCriteria None = new SnapshotSelectionCriteria(0L, 0L);

        public SnapshotSelectionCriteria(long maxSequenceNr = long.MaxValue, long maxTimeStamp = long.MaxValue) 
            : this()
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
        }

        public long MaxSequenceNr { get; private set; }
        public long MaxTimeStamp { get; private set; }
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
}