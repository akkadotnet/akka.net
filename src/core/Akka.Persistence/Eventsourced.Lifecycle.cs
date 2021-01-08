//-----------------------------------------------------------------------
// <copyright file="Eventsourced.Lifecycle.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
    public partial class Eventsourced
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Func<Envelope, bool> UnstashFilterPredicate =
            envelope => !(envelope.Message is WriteMessageSuccess || envelope.Message is ReplayedMessage);

        private void StartRecovery(Recovery recovery)
        {
            ChangeState(RecoveryStarted(recovery.ReplayMax));
            LoadSnapshot(SnapshotterId, recovery.FromSnapshot, recovery.ToSequenceNr);
        }

        private void RequestRecoveryPermit()
        {
            Extension.RecoveryPermitter().Tell(Akka.Persistence.RequestRecoveryPermit.Instance, Self);
            ChangeState(WaitingRecoveryPermit(Recovery));
        }

        protected internal override bool AroundReceive(Receive receive, object message)
        {
            _currentState.StateReceive(receive, message);
            return true;
        }

        /// <inheritdoc/>
        public override void AroundPreStart()
        {
            if (PersistenceId == null)
                throw new ArgumentNullException($"PersistenceId is [null] for PersistentActor [{Self.Path}]");
                
            // Fail fast on missing plugins.
            var j = Journal;
            var s = SnapshotStore;
            RequestRecoveryPermit();
            base.AroundPreStart();
        }

        /// <inheritdoc/>
        public override void AroundPreRestart(Exception cause, object message)
        {
            try
            {
                _internalStash.UnstashAll();
                Stash.UnstashAll(UnstashFilterPredicate);
            }
            finally
            {
                object inner;
                if (message is WriteMessageSuccess) inner = (message as WriteMessageSuccess).Persistent;
                else if (message is LoopMessageSuccess) inner = (message as LoopMessageSuccess).Message;
                else if (message is ReplayedMessage) inner = (message as ReplayedMessage).Persistent;
                else inner = message;

                FlushJournalBatch();
                base.AroundPreRestart(cause, inner);
            }
        }

        /// <inheritdoc/>
        public override void AroundPostRestart(Exception reason, object message)
        {
            RequestRecoveryPermit();
            base.AroundPostRestart(reason, message);
        }

        /// <inheritdoc/>
        public override void AroundPostStop()
        {
            try
            {
                _internalStash.UnstashAll();
                Stash.UnstashAll(UnstashFilterPredicate);
            }
            finally
            {
                base.AroundPostStop();
            }
        }

        /// <inheritdoc/>
        protected override void Unhandled(object message)
        {
            if (message is RecoveryCompleted) return; // ignore
            if (message is SaveSnapshotFailure)
            {
                var m = (SaveSnapshotFailure) message;
                if (Log.IsWarningEnabled)
                    Log.Warning("Failed to SaveSnapshot given metadata [{0}] due to: [{1}: {2}]", m.Metadata, m.Cause, m.Cause.Message);
            }
            if (message is DeleteSnapshotFailure)
            {
                var m = (DeleteSnapshotFailure) message;
                if (Log.IsWarningEnabled)
                    Log.Warning("Failed to DeleteSnapshot given metadata [{0}] due to: [{1}: {2}]", m.Metadata, m.Cause, m.Cause.Message);
            }
            if (message is DeleteSnapshotsFailure)
            {
                var m = (DeleteSnapshotsFailure) message;
                if (Log.IsWarningEnabled)
                    Log.Warning("Failed to DeleteSnapshots given criteria [{0}] due to: [{1}: {2}]", m.Criteria, m.Cause, m.Cause.Message);
            }
            if (message is DeleteMessagesFailure)
            {
                var m = (DeleteMessagesFailure) message;
                if (Log.IsWarningEnabled)
                    Log.Warning("Failed to DeleteMessages ToSequenceNr [{0}] for PersistenceId [{1}] due to: [{2}: {3}]", m.ToSequenceNr, PersistenceId, m.Cause, m.Cause.Message);
            }
            base.Unhandled(message);
        }
    }
}
