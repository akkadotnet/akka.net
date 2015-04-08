//-----------------------------------------------------------------------
// <copyright file="Snapshotter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// Orders to load a snapshots related to persistent actor identified by <paramref name="persistenceId"/>
        /// that match specified <paramref name="criteria"/> up to provided <paramref name="toSequenceNr"/> upper, inclusive bound.
        /// </summary>
        void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr);

        /// <summary>
        /// Saves <paramref name="snapshot"/> of current <see cref="ISnapshotter"/> state.
        /// If saving succeeds, this snapshotter will receive a <see cref="SaveSnapshotSuccess"/> message,
        /// otherwise <see cref="SaveSnapshotFailure"/> message.
        /// </summary>
        void SaveSnapshot(object snapshot);

        /// <summary>
        /// Deletes snapshot identified by <paramref name="sequenceNr"/> and <paramref name="timestamp"/>.
        /// </summary>
        void DeleteSnapshot(long sequenceNr, DateTime timestamp);

        /// <summary>
        /// Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// </summary>
        /// <param name="criteria"></param>
        void DeleteSnapshots(SnapshotSelectionCriteria criteria);
    }
}
