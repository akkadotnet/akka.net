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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class SnapshotMetadata : IEquatable<SnapshotMetadata>
    {
        /// <summary>
        /// This class represents an <see cref="IComparer{T}"/> used when comparing two <see cref="SnapshotMetadata"/> objects.
        /// </summary>
        internal class SnapshotMetadataComparer : IComparer<SnapshotMetadata>
        {
            /// <summary>
            /// Compares two objects and returns a value indicating whether one is less than, equal to, or greater than the other.
            /// </summary>
            /// <param name="x">The first object to compare.</param>
            /// <param name="y">The second object to compare.</param>
            /// <returns>
            /// A signed integer that indicates the relative values of <paramref name="x" /> and <paramref name="y" />, as shown in the following table.Value Meaning Less than zero<paramref name="x" /> is less than <paramref name="y" />.Zero<paramref name="x" /> equals <paramref name="y" />.Greater than zero<paramref name="x" /> is greater than <paramref name="y" />.
            /// </returns>
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
        public static readonly IComparer<SnapshotMetadata> Comparer = new SnapshotMetadataComparer();
        /// <summary>
        /// TBD
        /// </summary>
        public static DateTime TimestampNotSpecified = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotMetadata"/> class.
        /// </summary>
        /// <param name="persistenceId">The id of the persistent actor fro mwhich the snapshot was taken.</param>
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
        public readonly string PersistenceId;

        /// <summary>
        /// Sequence number at which a snapshot was taken.
        /// </summary>
        public readonly long SequenceNr;

        /// <summary>
        /// Time at which the snapshot was saved.
        /// </summary>
        public readonly DateTime Timestamp;

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotMetadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="SnapshotMetadata" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SnapshotMetadata" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SnapshotMetadata" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SnapshotMetadata other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(PersistenceId, other.PersistenceId) && SequenceNr == other.SequenceNr && Timestamp.Equals(other.Timestamp);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
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

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SnapshotMetadata<pid: {PersistenceId}, seqNr: {SequenceNr}, timestamp: {Timestamp:yyyy/MM/dd}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful saving of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshotSuccess : ISnapshotResponse, IEquatable<SaveSnapshotSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        public SaveSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;

        /// <summary>
        /// Determines whether the specified <see cref="SaveSnapshotSuccess" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SaveSnapshotSuccess" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SaveSnapshotSuccess" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SaveSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshotSuccess);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return (Metadata != null ? Metadata.GetHashCode() : 0);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SaveSnapshotSuccess<{Metadata}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        public DeleteSnapshotSuccess(SnapshotMetadata metadata)
        {
            Metadata = metadata;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshotSuccess" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshotSuccess" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshotSuccess" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>

        public bool Equals(DeleteSnapshotSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotSuccess);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return (Metadata != null ? Metadata.GetHashCode() : 0);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"DeleteSnapshotSuccess<{Metadata}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after successful deletion of a specified range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsSuccess : ISnapshotResponse, IEquatable<DeleteSnapshotsSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="criteria">TBD</param>
        public DeleteSnapshotsSuccess(SnapshotSelectionCriteria criteria)
        {
            Criteria = criteria;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotSelectionCriteria Criteria;

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshotsSuccess" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshotsSuccess" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshotsSuccess" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(DeleteSnapshotsSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Criteria, other.Criteria);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotsSuccess);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return (Criteria != null ? Criteria.GetHashCode() : 0);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
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
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="cause">TBD</param>
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

        /// <summary>
        /// Determines whether the specified <see cref="SaveSnapshotFailure" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SaveSnapshotFailure" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SaveSnapshotFailure" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SaveSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshotFailure);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
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
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="cause">TBD</param>
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

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshotFailure" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshotFailure" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshotFailure" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(DeleteSnapshotFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Metadata, other.Metadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotFailure);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"DeleteSnapshotFailure<meta: {Metadata}, cause: {Cause}>";
        }
    }

    /// <summary>
    /// Sent to <see cref="PersistentActor"/> after failed deletion of a range of snapshots.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshotsFailure : ISnapshotResponse, IEquatable<DeleteSnapshotsFailure>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="criteria">TBD</param>
        /// <param name="cause">TBD</param>
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

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshotsFailure" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshotsFailure" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshotsFailure" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(DeleteSnapshotsFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && Equals(Criteria, other.Criteria);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshotsFailure);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Criteria != null ? Criteria.GetHashCode() : 0) * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"DeleteSnapshotsFailure<criteria: {Criteria}, cause: {Cause}>";
        }
    }

    /// <summary>
    /// Offers a <see cref="PersistentActor"/> a previously saved snapshot during recovery.
    /// This offer is received before any further replayed messages.
    /// </summary>
    [Serializable]
    public sealed class SnapshotOffer : IEquatable<SnapshotOffer>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="snapshot">TBD</param>
        public SnapshotOffer(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object Snapshot;

        /// <summary>
        /// Determines whether the specified <see cref="SnapshotOffer" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SnapshotOffer" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SnapshotOffer" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SnapshotOffer other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotOffer);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SnapshotOffer<meta: {Metadata}, snapshot: {Snapshot}>";
        }
    }

    /// <summary>
    /// Selection criteria for loading and deleting a snapshots.
    /// </summary>
    [Serializable]
    public sealed class SnapshotSelectionCriteria : IEquatable<SnapshotSelectionCriteria>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SnapshotSelectionCriteria Latest = new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SnapshotSelectionCriteria None = new SnapshotSelectionCriteria(0L, DateTime.MinValue);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxSequenceNr">TBD</param>
        /// <param name="maxTimeStamp">TBD</param>
        /// <param name="minSequenceNr">TBD</param>
        /// <param name="minTimestamp">TBD</param>
        [JsonConstructor]
        public SnapshotSelectionCriteria(long maxSequenceNr, DateTime maxTimeStamp, long minSequenceNr = 0L, DateTime? minTimestamp = null)
        {
            MaxSequenceNr = maxSequenceNr;
            MaxTimeStamp = maxTimeStamp;
            MinSequenceNr = minSequenceNr;
            MinTimestamp = minTimestamp ?? DateTime.MinValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxSequenceNr">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        internal SnapshotSelectionCriteria Limit(long toSequenceNr)
        {
            return toSequenceNr < MaxSequenceNr
                ? new SnapshotSelectionCriteria(toSequenceNr, MaxTimeStamp)
                : this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <returns>TBD</returns>
        internal bool IsMatch(SnapshotMetadata metadata)
        {
            return metadata.SequenceNr <= MaxSequenceNr && metadata.Timestamp <= MaxTimeStamp &&
                metadata.SequenceNr >= MinSequenceNr && metadata.Timestamp >= MinTimestamp;
        }

        /// <summary>
        /// Determines whether the specified <see cref="SnapshotSelectionCriteria" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SnapshotSelectionCriteria" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SnapshotSelectionCriteria" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SnapshotSelectionCriteria other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return MaxSequenceNr == other.MaxSequenceNr && MaxTimeStamp == other.MaxTimeStamp &&
                MinSequenceNr == other.MinSequenceNr && MinTimestamp == other.MinTimestamp;
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SnapshotSelectionCriteria);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
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

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SnapshotSelectionCriteria<maxSeqNr: {MaxSequenceNr}, maxTimestamp: {MaxTimeStamp:yyyy/MM/dd}, minSeqNr: {MinSequenceNr}, minTimestamp: {MinTimestamp:yyyy/MM/dd}>";
        }
    }

    /// <summary>
    /// A selected snapshot matching <see cref="SnapshotSelectionCriteria"/>.
    /// </summary>
    [Serializable]
    public sealed class SelectedSnapshot : IEquatable<SelectedSnapshot>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="snapshot">TBD</param>
        public SelectedSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object Snapshot;

        /// <summary>
        /// Determines whether the specified <see cref="SelectedSnapshot" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SelectedSnapshot" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SelectedSnapshot" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SelectedSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SelectedSnapshot);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SelectedSnapshot<meta: {Metadata}, snapshot: {Snapshot}>";
        }
    }


    #region Internal API for Snapshot protocol

    /// <summary>
    /// Instructs a snapshot store to load the snapshot.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshot: ISnapshotRequest, IEquatable<LoadSnapshot>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
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

        /// <summary>
        /// Determines whether the specified <see cref="LoadSnapshot" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="LoadSnapshot" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="LoadSnapshot" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(LoadSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as LoadSnapshot);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
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

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"LoadSnapshot<pid: {PersistenceId}, toSeqNr: {ToSequenceNr}, criteria: {Criteria}>";
        }
    }

    /// <summary>
    /// Response to a <see cref="LoadSnapshot"/> message.
    /// </summary>
    [Serializable]
    public sealed class LoadSnapshotResult : ISnapshotResponse, IEquatable<LoadSnapshotResult>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="snapshot">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        public LoadSnapshotResult(SelectedSnapshot snapshot, long toSequenceNr)
        {
            Snapshot = snapshot;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// Loaded snapshot or null if none provided.
        /// </summary>
        public readonly SelectedSnapshot Snapshot;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long ToSequenceNr;

        /// <summary>
        /// Determines whether the specified <see cref="LoadSnapshotResult" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="LoadSnapshotResult" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="LoadSnapshotResult" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(LoadSnapshotResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ToSequenceNr, other.ToSequenceNr)
                && Equals(Snapshot, other.Snapshot);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as LoadSnapshotResult);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Snapshot != null ? Snapshot.GetHashCode() : 0) * 397) ^ ToSequenceNr.GetHashCode();
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"LoadSnapshotResult<toSeqNr: {ToSequenceNr}, snapshot: {Snapshot}>";
        }
    }

    /// <summary>
    /// Instructs a snapshot store to save a snapshot.
    /// </summary>
    [Serializable]
    public sealed class SaveSnapshot : ISnapshotRequest, IEquatable<SaveSnapshot>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="snapshot">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public SaveSnapshot(SnapshotMetadata metadata, object snapshot)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata), "SaveSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
            Snapshot = snapshot;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object Snapshot;

        /// <summary>
        /// Determines whether the specified <see cref="SaveSnapshot" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="SaveSnapshot" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="SaveSnapshot" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(SaveSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata) && Equals(Snapshot, other.Snapshot);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SaveSnapshot);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metadata != null ? Metadata.GetHashCode() : 0) * 397) ^ (Snapshot != null ? Snapshot.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"SaveSnapshot<meta: {Metadata}, snapshot: {Snapshot}>";
        }
    }

    /// <summary>
    /// Instructs a snapshot store to delete a snapshot.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshot : ISnapshotRequest, IEquatable<DeleteSnapshot>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public DeleteSnapshot(SnapshotMetadata metadata)
        {
            if(metadata == null)
                throw new ArgumentNullException(nameof(metadata), "DeleteSnapshot requires SnapshotMetadata to be provided");

            Metadata = metadata;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotMetadata Metadata;

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshot" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshot" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshot" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(DeleteSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Metadata, other.Metadata);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshot);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return Metadata.GetHashCode();
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"DeleteSnapshot<meta: {Metadata}>";
        }
    }

    /// <summary>
    /// Instructs a snapshot store to delete all snapshots that match provided criteria.
    /// </summary>
    [Serializable]
    public sealed class DeleteSnapshots : ISnapshotRequest, IEquatable<DeleteSnapshots>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        public DeleteSnapshots(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            PersistenceId = persistenceId;
            Criteria = criteria;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly SnapshotSelectionCriteria Criteria;

        /// <summary>
        /// Determines whether the specified <see cref="DeleteSnapshots" />, is equal to this instance.
        /// </summary>
        /// <param name="other">The <see cref="DeleteSnapshots" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="DeleteSnapshots" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(DeleteSnapshots other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(Criteria, other.Criteria);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteSnapshots);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((PersistenceId != null ? PersistenceId.GetHashCode() : 0) * 397) ^ (Criteria != null ? Criteria.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"DeleteSnapshots<pid: {PersistenceId}, criteria: {Criteria}>";
        }
    }

    #endregion
}

