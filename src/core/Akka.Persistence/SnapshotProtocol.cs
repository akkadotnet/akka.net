//-----------------------------------------------------------------------
// <copyright file="SnapshotProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

    /// <summary>
    /// Metadata for all persisted snapshot records.
    /// </summary>
    [Serializable]
    public sealed class SnapshotMetadata : IEquatable<SnapshotMetadata>
    {
        /// <summary>
        /// This class represents an <see cref="IComparer{T}"/> used when comparing two <see cref="SnapshotMetadata"/> objects.
        /// </summary>
        internal class SnapshotMetadataComparer : IComparer<SnapshotMetadata>
        {
            /// <inheritdoc/>
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

        /// <summary>
        /// TBD
        /// </summary>
        public static IComparer<SnapshotMetadata> Comparer { get; } = new SnapshotMetadataComparer();

        /// <summary>
        /// TBD
        /// </summary>
        public static DateTime TimestampNotSpecified = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotMetadata"/> class.
        /// </summary>
        /// <param name="persistenceId">The id of the persistent actor fro which the snapshot was taken.</param>
        /// <param name="sequenceNr">The sequence number at which the snapshot was taken.</param>
        public SnapshotMetadata(string persistenceId, long sequenceNr)
            : this(persistenceId, sequenceNr, TimestampNotSpecified)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotMetadata"/> class.
        /// </summary>
        /// <param name="persistenceId">The id of the persistent actor fro mwhich the snapshot was taken.</param>
        /// <param name="sequenceNr">The sequence number at which the snapshot was taken.</param>
        /// <param name="timestamp">The time at which the snapshot was saved.</param>
        [JsonConstructor]
        public SnapshotMetadata(string persistenceId, long sequenceNr, DateTime timestamp)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
        }

        /// <summary>
        /// Id of the persistent actor from which the snapshot was taken.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Sequence number at which a snapshot was taken.
        /// </summary>
        public long SequenceNr { get; }

        /// <summary>
        /// Time at which the snapshot was saved.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SnapshotMetadata);

        /// <inheritdoc/>
        public bool Equals(SnapshotMetadata other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(PersistenceId, other.PersistenceId) && SequenceNr == other.SequenceNr && Timestamp.Equals(other.Timestamp);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString() => $"SnapshotMetadata<pid: {PersistenceId}, seqNr: {SequenceNr}, timestamp: {Timestamp:yyyy/MM/dd}>";
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful saving of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshotSuccess : ISnapshotResponse, IEquatable<SaveSnapshotSuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SaveSnapshotSuccess"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        public SaveSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <inheritdoc/>
        public bool Equals(SaveSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SaveSnapshotSuccess);

        /// <inheritdoc/>
        public override int GetHashCode() => Metadata != null ? Metadata.GetHashCode() : 0;

        /// <inheritdoc/>
        public override string ToString() => $"SaveSnapshotSuccess<{Metadata}>";
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotSuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshotSuccess"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        public DeleteSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteSnapshotSuccess);

        /// <inheritdoc/>
        public override int GetHashCode() => Metadata != null ? Metadata.GetHashCode() : 0;

        /// <inheritdoc/>
        public override string ToString() => $"DeleteSnapshotSuccess<{Metadata}>";
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a specified range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotsSuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshotsSuccess"/> class.
        /// </summary>
        /// <param name="criteria">Snapshot selection criteria.</param>
        public DeleteSnapshotsSuccess(SnapshotSelectionCriteria criteria)
        {
            Criteria = criteria;
        }

        /// <summary>
        /// Snapshot selection criteria.
        /// </summary>
        public SnapshotSelectionCriteria Criteria { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshotsSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Criteria, other.Criteria);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotsSuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Criteria != null ? Criteria.GetHashCode() : 0);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"DeleteSnapshotsSuccess<{Criteria}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed saving a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshotFailure : ISnapshotResponse, IEquatable<SaveSnapshotFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SaveSnapshotFailure"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="cause">A failure cause.</param>
        public SaveSnapshotFailure(SnapshotMetadata metadata, Exception cause)
        {
            Metadata = metadata;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <summary>
        /// A failure cause.
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public bool Equals(SaveSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshotFailure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"SaveSnapshotFailure<meta: {Metadata}, cause: {Cause}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed deletion of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotFailure : ISnapshotResponse, IEquatable<DeleteSnapshotFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshotFailure"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="cause">A failure cause.</param>
        public DeleteSnapshotFailure(SnapshotMetadata metadata, Exception cause)
        {
            Metadata = metadata;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <summary>
        /// A failure cause.
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteSnapshotFailure);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"DeleteSnapshotFailure<meta: {Metadata}, cause: {Cause}>";
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed deletion of a range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsFailure : ISnapshotResponse, IEquatable<DeleteSnapshotsFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshotsFailure"/> class.
        /// </summary>
        /// <param name="criteria">Snapshot selection criteria.</param>
        /// <param name="cause">A failure cause.</param>
        public DeleteSnapshotsFailure(SnapshotSelectionCriteria criteria, Exception cause)
        {
            Criteria = criteria;
            Cause = cause;
        }

        /// <summary>
        /// Snapshot selection criteria.
        /// </summary>
        public SnapshotSelectionCriteria Criteria { get; }

        /// <summary>
        /// A failure cause.
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshotsFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Criteria, other.Criteria);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteSnapshotsFailure);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Criteria != null ? Criteria.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"DeleteSnapshotsFailure<criteria: {Criteria}, cause: {Cause}>";
    }

    /// <summary>
    /// Offers a <see cref="PersistentActor"/> a previously saved snapshot during recovery.
    /// This offer is received before any further replayed messages.
    /// </summary>
    [Serializable]
    public sealed class SnapshotOffer : IEquatable<SnapshotOffer>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotOffer"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        public SnapshotOffer(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <summary>
        /// Snapshot.
        /// </summary>
        public object Snapshot { get; }

        /// <inheritdoc/>
        public bool Equals(SnapshotOffer other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SnapshotOffer);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"SnapshotOffer<meta: {Metadata}, snapshot: {Snapshot}>";
    }

    /// <summary>
    /// Selection criteria for loading and deleting a snapshots.
    /// </summary>
    [Serializable]
    public sealed class SnapshotSelectionCriteria : IEquatable<SnapshotSelectionCriteria>
    {
        /// <summary>
        /// The latest saved snapshot.
        /// </summary>
        public static SnapshotSelectionCriteria Latest { get; } = new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue);

        /// <summary>
        /// No saved snapshot matches.
        /// </summary>
        public static SnapshotSelectionCriteria None { get; } = new SnapshotSelectionCriteria(0L, DateTime.MinValue);

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotSelectionCriteria"/> class.
        /// </summary>
        /// <param name="maxSequenceNr">Upper bound for a selected snapshot's sequence number.</param>
        /// <param name="maxTimeStamp">Upper bound for a selected snapshot's timestamp.</param>
        /// <param name="minSequenceNr">Lower bound for a selected snapshot's sequence number</param>
        /// <param name="minTimestamp">Lower bound for a selected snapshot's timestamp</param>
        [JsonConstructor]
        public SnapshotSelectionCriteria(long maxSequenceNr, DateTime maxTimeStamp, long minSequenceNr = 0L, DateTime? minTimestamp = null)
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
            MinSequenceNr = minSequenceNr;
            MinTimestamp = minTimestamp ?? DateTime.MinValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotSelectionCriteria"/> class.
        /// </summary>
        /// <param name="maxSequenceNr">Upper bound for a selected snapshot's sequence number.</param>
        public SnapshotSelectionCriteria(long maxSequenceNr) : this(maxSequenceNr, DateTime.MaxValue)
        {
        }

        /// <summary>
        /// Upper bound for a selected snapshot's sequence number.
        /// </summary>
        public long MaxSequenceNr { get; }

        /// <summary>
        /// Upper bound for a selected snapshot's timestamp.
        /// </summary>
        public DateTime MaxTimeStamp { get; }

        /// <summary>
        /// Lower bound for a selected snapshot's sequence number
        /// </summary>
        public long MinSequenceNr { get; }

        /// <summary>
        /// Lower bound for a selected snapshot's timestamp
        /// </summary>
        public DateTime? MinTimestamp { get; }

        internal SnapshotSelectionCriteria Limit(long toSequenceNr)
        {
            return toSequenceNr < MaxSequenceNr
                ? new SnapshotSelectionCriteria(toSequenceNr, MaxTimeStamp, MinSequenceNr, MinTimestamp)
                : this;
        }

        internal bool IsMatch(SnapshotMetadata metadata)
        {
            return metadata.SequenceNr <= MaxSequenceNr && metadata.Timestamp <= MaxTimeStamp &&
                   metadata.SequenceNr >= MinSequenceNr && metadata.Timestamp >= MinTimestamp;
        }

        /// <inheritdoc/>
        public bool Equals(SnapshotSelectionCriteria other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return MaxSequenceNr == other.MaxSequenceNr && MaxTimeStamp == other.MaxTimeStamp &&
                   MinSequenceNr == other.MinSequenceNr && MinTimestamp == other.MinTimestamp;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SnapshotSelectionCriteria);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString() => $"SnapshotSelectionCriteria<maxSeqNr: {MaxSequenceNr}, maxTimestamp: {MaxTimeStamp:yyyy/MM/dd}, minSeqNr: {MinSequenceNr}, minTimestamp: {MinTimestamp:yyyy/MM/dd}>";
    }

    /// <summary>
    /// A selected snapshot matching <see cref="SnapshotSelectionCriteria"/>.
    /// </summary>
    [Serializable]
    public sealed class SelectedSnapshot : IEquatable<SelectedSnapshot>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectedSnapshot"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        public SelectedSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <summary>
        /// Snapshot.
        /// </summary>
        public object Snapshot { get; }

        /// <inheritdoc/>
        public bool Equals(SelectedSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SelectedSnapshot);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"SelectedSnapshot<meta: {Metadata}, snapshot: {Snapshot}>";
    }

    /// <summary>
    /// Instructs a snapshot store to load the snapshot.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshot : ISnapshotRequest, IEquatable<LoadSnapshot>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadSnapshot"/> class.
        /// </summary>
        /// <param name="persistenceId">Persistent actor identifier.</param>
        /// <param name="criteria">Criteria for selecting snapshot, from which the recovery should start.</param>
        /// <param name="toSequenceNr">Upper, inclusive sequence number bound for recovery.</param>
        public LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Persistent actor identifier.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Criteria for selecting snapshot, from which the recovery should start.
        /// </summary>
        public SnapshotSelectionCriteria Criteria { get; }

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <inheritdoc/>
        public bool Equals(LoadSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as LoadSnapshot);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString() => $"LoadSnapshot<pid: {PersistenceId}, toSeqNr: {ToSequenceNr}, criteria: {Criteria}>";
    }

    /// <summary>
    /// Response to a <see cref="LoadSnapshot"/> message.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshotResult : ISnapshotResponse, IEquatable<LoadSnapshotResult>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadSnapshotResult"/> class.
        /// </summary>
        /// <param name="snapshot">Loaded snapshot or null if none provided.</param>
        /// <param name="toSequenceNr">Upper sequence number bound (inclusive) for recovery.</param>
        public LoadSnapshotResult(SelectedSnapshot snapshot, long toSequenceNr)
        {
            Snapshot = snapshot;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Loaded snapshot or null if none provided.
        /// </summary>
        public SelectedSnapshot Snapshot { get; }

        /// <summary>
        /// Upper sequence number bound (inclusive) for recovery.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <inheritdoc/>
        public bool Equals(LoadSnapshotResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(Snapshot, other.Snapshot);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as LoadSnapshotResult);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Snapshot != null ? Snapshot.GetHashCode() : 0) * 397) ^ ToSequenceNr.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"LoadSnapshotResult<toSeqNr: {ToSequenceNr}, snapshot: {Snapshot}>";
    }

    /// <summary>
    /// Reply message to a failed <see cref="LoadSnapshot"/> request.
    /// </summary>
    public sealed class LoadSnapshotFailed : ISnapshotResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadSnapshotFailed"/> class.
        /// </summary>
        /// <param name="cause">Failure cause.</param>
        public LoadSnapshotFailed(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>
        /// Failure cause.
        /// </summary>
        public Exception Cause { get; }

        private bool Equals(LoadSnapshotFailed other) => Equals(Cause, other.Cause);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is LoadSnapshotFailed && Equals((LoadSnapshotFailed)obj);
        }

        public override int GetHashCode() => Cause?.GetHashCode() ?? 0;

        public override string ToString() => $"LoadSnapshotFailed<Cause: {Cause}>";
    }

    /// <summary>
    /// Instructs a snapshot store to save a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshot : ISnapshotRequest, IEquatable<SaveSnapshot>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SaveSnapshot"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="metadata"/> is undefined.
        /// </exception>
        public SaveSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata), "SaveSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <summary>
        /// Snapshot.
        /// </summary>
        public object Snapshot { get; }

        /// <inheritdoc/>
        public bool Equals(SaveSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SaveSnapshot);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"SaveSnapshot<meta: {Metadata}, snapshot: {Snapshot}>";
    }

    /// <summary>
    /// Instructs a snapshot store to delete a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshot : ISnapshotRequest, IEquatable<DeleteSnapshot>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshot"/> class.
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="metadata"/> is undefined.
        /// </exception>
        public DeleteSnapshot(SnapshotMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata), "DeleteSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
        }

        /// <summary>
        /// Snapshot metadata.
        /// </summary>
        public SnapshotMetadata Metadata { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteSnapshot);

        /// <inheritdoc/>
        public override int GetHashCode() => Metadata.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"DeleteSnapshot<meta: {Metadata}>";
    }

    /// <summary>
    /// Instructs a snapshot store to delete all snapshots that match provided criteria.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshots : ISnapshotRequest, IEquatable<DeleteSnapshots>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteSnapshots"/> class.
        /// </summary>
        /// <param name="persistenceId">Persistent actor id.</param>
        /// <param name="criteria">Criteria for selecting snapshots to be deleted.</param>
        public DeleteSnapshots(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
        }

        /// <summary>
        /// Persistent actor id.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Criteria for selecting snapshots to be deleted.
        /// </summary>
        public SnapshotSelectionCriteria Criteria { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteSnapshots other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteSnapshots);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((PersistenceId != null ? PersistenceId.GetHashCode() : 0) * 397) ^ (Criteria != null ? Criteria.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"DeleteSnapshots<pid: {PersistenceId}, criteria: {Criteria}>";
    }
}
