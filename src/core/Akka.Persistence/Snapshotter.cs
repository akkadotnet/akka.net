//-----------------------------------------------------------------------
// <copyright file="Snapshotter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence
{
    /// <summary>
    /// Snapshot API on top of the internal snapshot protocol.
    /// </summary>
    public interface ISnapshotter
    {
        /// <summary>
        /// Snapshotter id.
        /// </summary>
        string SnapshotterId { get; }

        /// <summary>
        /// Incrementable sequence number to use when taking a snapshot.
        /// </summary>
        long SnapshotSequenceNr { get; }

        /// <summary>
        /// Instructs the snapshot store to load the specified snapshot and send it via an
        /// <see cref="SnapshotOffer"/> to the running <see cref="PersistentActor"/>.
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr);

        /// <summary>
        /// Saves <paramref name="snapshot"/> of current <see cref="ISnapshotter"/> state.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the success or failure of this
        /// via an <see cref="SaveSnapshotSuccess"/> or <see cref="SaveSnapshotFailure"/> message.
        /// </summary>
        /// <param name="snapshot">TBD</param>
        void SaveSnapshot(object snapshot);

        /// <summary>
        /// Deletes the snapshot identified by <paramref name="sequenceNr"/>.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the status of the deletion
        /// via an <see cref="DeleteSnapshotSuccess"/> or <see cref="DeleteSnapshotFailure"/> message.
        /// </summary>
        /// <param name="sequenceNr">TBD</param>
        void DeleteSnapshot(long sequenceNr);

        /// <summary>
        /// Deletes all snapshots matching <paramref name="criteria"/>.
        /// 
        /// The <see cref="PersistentActor"/> will be notified about the status of the deletion
        /// via an <see cref="DeleteSnapshotsSuccess"/> or <see cref="DeleteSnapshotsFailure"/> message.
        /// </summary>
        /// <param name="criteria">TBD</param>
        void DeleteSnapshots(SnapshotSelectionCriteria criteria);
    }
}
