//-----------------------------------------------------------------------
// <copyright file="SnapshotProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Akka.Persistence
{
    /// <summary>
    /// Marker interface for internal snapshot messages
    /// </summary>
    public interface ISnapshotMessage : IPersistenceMessage { }

    /// <summary>
    /// Internal snapshot command
    /// </summary>
    public interface ISnapshotRequest : ISnapshotMessage { }

    /// <summary>
    /// Internal snapshot acknowledgement
    /// </summary>
    public interface ISnapshotResponse : ISnapshotMessage { }

    [Serializable]
    public sealed class SnapshotMetadata : IEquatable<SnapshotMetadata>
    {
        internal class SnapshotMetadataComparer : IComparer<SnapshotMetadata>
        {
            public int Compare(SnapshotMetadata x, SnapshotMetadata y)
            {
                var compare = string.Compare(x.PersistenceId, y.PersistenceId, StringComparison.Ordinal);
                if (compare == 0)
                {
                    compare = Math.Sign(x.SequenceNr - y.SequenceNr);
                    if (compare == 0)
                        compare = x.Timestamp.CompareTo(y.Timestamp);
                }
                return compare;
            }
        }

        public static readonly IComparer<SnapshotMetadata> Comparer = new SnapshotMetadataComparer();
        public static DateTime TimestampNotSpecified = DateTime.MinValue;

        public SnapshotMetadata(string persistenceId, long sequenceNr)
            : this(persistenceId, sequenceNr, TimestampNotSpecified)
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
        public readonly string PersistenceId;

        /// <summary>
        /// Sequence number at which a snapshot was taken.
        /// </summary>
        public readonly long SequenceNr;

        /// <summary>
        /// Time at which the snapshot was saved.
        /// </summary>
        public readonly DateTime Timestamp;

        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotMetadata);
        }

        public bool Equals(SnapshotMetadata other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

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

        public override string ToString()
        {
            return string.Format("SnapshotMetadata<pid: {0}, seqNr: {1}, timestamp: {2:yyyy/MM/dd}>", PersistenceId, SequenceNr, Timestamp);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful saving of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshotSuccess : ISnapshotResponse, IEquatable<SaveSnapshotSuccess>
    {
        public SaveSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        public readonly SnapshotMetadata Metadata;
        
        public bool Equals(SaveSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshotSuccess);
        }

        public override int GetHashCode()
        {
            return (Metadata != null ? Metadata.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("SaveSnapshotSuccess<{0}>", Metadata);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotSuccess>
    {
        public DeleteSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        public readonly SnapshotMetadata Metadata;
        
        public bool Equals(DeleteSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotSuccess);
        }

        public override int GetHashCode()
        {
            return (Metadata != null ? Metadata.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshotSuccess<{0}>", Metadata);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a specified range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotsSuccess>
    {
        public DeleteSnapshotsSuccess(SnapshotSelectionCriteria criteria)
        {
            Criteria = criteria;
        }

        public readonly SnapshotSelectionCriteria Criteria;
        
        public bool Equals(DeleteSnapshotsSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Criteria, other.Criteria);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotsSuccess);
        }

        public override int GetHashCode()
        {
            return (Criteria != null ? Criteria.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshotsSuccess<{0}>", Criteria);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed saving a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshotFailure : ISnapshotResponse, IEquatable<SaveSnapshotFailure>
    {
        public SaveSnapshotFailure(SnapshotMetadata metadata, Exception cause)
        {
            Metadata = metadata;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public readonly SnapshotMetadata Metadata;

        /// <summary>
        /// A failure cause.
        /// </summary>
        public readonly Exception Cause;

        public bool Equals(SaveSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshotFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("SaveSnapshotFailure<meta: {0}, cause: {1}>", Metadata, Cause);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed deletion of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotFailure : ISnapshotResponse, IEquatable<DeleteSnapshotFailure>
    {
        public DeleteSnapshotFailure(SnapshotMetadata metadata, Exception cause)
        {
            Metadata = metadata;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public readonly SnapshotMetadata Metadata;

        /// <summary>
        /// A failure cause.
        /// </summary>
        public readonly Exception Cause;

        public bool Equals(DeleteSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshotFailure<meta: {0}, cause: {1}>", Metadata, Cause);
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed deletion of a range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsFailure : ISnapshotResponse, IEquatable<DeleteSnapshotsFailure>
    {
        public DeleteSnapshotsFailure(SnapshotSelectionCriteria criteria, Exception cause)
        {
            Criteria = criteria;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public readonly SnapshotSelectionCriteria Criteria;

        /// <summary>
        /// A failure cause.
        /// </summary>
        public readonly Exception Cause;

        public bool Equals(DeleteSnapshotsFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Criteria, other.Criteria);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotsFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Criteria != null ? Criteria.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshotsFailure<criteria: {0}, cause: {1}>", Criteria, Cause);
        }
    }

    /// <summary>
    /// Offers a <see cref="PersistentActor"/> a previously saved snapshot during recovery.
    /// This offer is received before any further replayed messages.
    /// </summary>
    [Serializable]
    public sealed class SnapshotOffer : IEquatable<SnapshotOffer>
    {
        public SnapshotOffer(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public readonly SnapshotMetadata Metadata;
        public readonly object Snapshot;

        public bool Equals(SnapshotOffer other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotOffer);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("SnapshotOffer<meta: {0}, snapshot: {1}>", Metadata, Snapshot);
        }
    }

    /// <summary>
    /// Selection criteria for loading and deleting a snapshots.
    /// </summary>
    [Serializable]
    public sealed class SnapshotSelectionCriteria : IEquatable<SnapshotSelectionCriteria>
    {
        public static readonly SnapshotSelectionCriteria Latest = new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue);
        public static readonly SnapshotSelectionCriteria None = new SnapshotSelectionCriteria(0L, DateTime.MinValue);

        [JsonConstructor]
        public SnapshotSelectionCriteria(long maxSequenceNr, DateTime maxTimeStamp, long minSequenceNr = 0L, DateTime? minTimestamp = null)
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
            MinSequenceNr = minSequenceNr;
            MinTimestamp = minTimestamp ?? DateTime.MinValue;
        }

        public SnapshotSelectionCriteria(long maxSequenceNr) : this(maxSequenceNr, DateTime.MaxValue)
        {
        }

        /// <summary>
        /// Upper bound for a selected snapshot's sequence number.
        /// </summary>
        public readonly long MaxSequenceNr;

        /// <summary>
        /// Upper bound for a selected snapshot's timestamp.
        /// </summary>
        public readonly DateTime MaxTimeStamp;

        /// <summary>
        /// Lower bound for a selected snapshot's sequence number
        /// </summary>
        public readonly long MinSequenceNr;

        /// <summary>
        /// Lower bound for a selected snapshot's timestamp
        /// </summary>
        public readonly DateTime? MinTimestamp;

        internal SnapshotSelectionCriteria Limit(long toSequenceNr)
        {
            return toSequenceNr < MaxSequenceNr
                ? new SnapshotSelectionCriteria(toSequenceNr, MaxTimeStamp)
                : this;
        }

        internal bool IsMatch(SnapshotMetadata metadata)
        {
            return metadata.SequenceNr <= MaxSequenceNr && metadata.Timestamp <= MaxTimeStamp &&
                metadata.SequenceNr >= MinSequenceNr && metadata.Timestamp >= MinTimestamp;
        }

        public bool Equals(SnapshotSelectionCriteria other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return MaxSequenceNr == other.MaxSequenceNr && MaxTimeStamp == other.MaxTimeStamp &&
                MinSequenceNr == other.MinSequenceNr && MinTimestamp == other.MinTimestamp;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotSelectionCriteria);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + MaxSequenceNr.GetHashCode();
                hash = hash * 23 + MaxTimeStamp.GetHashCode();
                hash = hash * 23 + MinSequenceNr.GetHashCode();
                hash = hash * 23 + MinTimestamp.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                "SnapshotSelectionCriteria<maxSeqNr: {0}, maxTimestamp: {1:yyyy/MM/dd}, minSeqNr: {2}, minTimestamp: {3:yyyy/MM/dd}>",
                MaxSequenceNr, MaxTimeStamp, MinSequenceNr, MinTimestamp);
        }
    }

    /// <summary>
    /// A selected snapshot matching <see cref="SnapshotSelectionCriteria"/>.
    /// </summary>
    [Serializable]
    public sealed class SelectedSnapshot : IEquatable<SelectedSnapshot>
    {
        public SelectedSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        public readonly SnapshotMetadata Metadata;
        public readonly object Snapshot;

        public bool Equals(SelectedSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SelectedSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("SelectedSnapshot<meta: {0}, snapshot: {1}>", Metadata, Snapshot);
        }
    }


    #region Internal API for Snapshot protocol

    /// <summary>
    /// Instructs a snapshot store to load the snapshot.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshot: ISnapshotRequest, IEquatable<LoadSnapshot>
    {
        public LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Persistent actor identifier.
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// Criteria for selecting snapshot, from which the recovery should start.
        /// </summary>
        public readonly SnapshotSelectionCriteria Criteria;

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery.
        /// </summary>
        public readonly long ToSequenceNr;

        public bool Equals(LoadSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LoadSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Criteria != null ? Criteria.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ToSequenceNr.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("LoadSnapshot<pid: {0}, toSeqNr: {1}, criteria: {2}>", PersistenceId, ToSequenceNr, Criteria);
        }
    }

    /// <summary>
    /// Response to a <see cref="LoadSnapshot"/> message.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshotResult : ISnapshotResponse, IEquatable<LoadSnapshotResult>
    {
        public LoadSnapshotResult(SelectedSnapshot snapshot, long toSequenceNr)
        {
            Snapshot = snapshot;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Loaded snapshot or null if none provided.
        /// </summary>
        public readonly SelectedSnapshot Snapshot;
        public readonly long ToSequenceNr;
        
        public bool Equals(LoadSnapshotResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                && Equals(Snapshot, other.Snapshot);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LoadSnapshotResult);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Snapshot != null ? Snapshot.GetHashCode() : 0) * 397) ^ ToSequenceNr.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("LoadSnapshotResult<toSeqNr: {0}, snapshot: {1}>", ToSequenceNr, Snapshot);
        }
    }

    /// <summary>
    /// Instructs a snapshot store to save a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshot : ISnapshotRequest, IEquatable<SaveSnapshot>
    {
        public SaveSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            if (metadata == null)
                throw new ArgumentNullException("metadata", "SaveSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
            Snapshot = snapshot;
        }

        public readonly SnapshotMetadata Metadata;
        public readonly object Snapshot;

        public bool Equals(SaveSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("SaveSnapshot<meta: {0}, snapshot: {1}>", Metadata, Snapshot);
        }
    }

    /// <summary>
    /// Instructs a snapshot store to delete a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshot : ISnapshotRequest, IEquatable<DeleteSnapshot>
    {
        public DeleteSnapshot(SnapshotMetadata metadata)
        {
            if(metadata == null)
                throw new ArgumentNullException("metadata", "DeleteSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
        }

        public readonly SnapshotMetadata Metadata;

        public bool Equals(DeleteSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshot);
        }

        public override int GetHashCode()
        {
            return Metadata.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshot<meta: {0}>", Metadata);
        }
    }

    /// <summary>
    /// Instructs a snapshot store to delete all snapshots that match provided criteria.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshots : ISnapshotRequest, IEquatable<DeleteSnapshots>
    {
        public DeleteSnapshots(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
        }

        public readonly string PersistenceId;
        public readonly SnapshotSelectionCriteria Criteria;

        public bool Equals(DeleteSnapshots other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshots);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((PersistenceId != null ? PersistenceId.GetHashCode() : 0) * 397) ^ (Criteria != null ? Criteria.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("DeleteSnapshots<pid: {0}, criteria: {1}>", PersistenceId, Criteria);
        }
    }

    #endregion
}

