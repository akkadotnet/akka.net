//-----------------------------------------------------------------------
// <copyright file="PersistentView.Lifecycle.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
    public partial class PersistentView
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            base.PreStart();
            StartRecovery(Recovery);

            if (IsAutoUpdate)
            {
                _scheduleCancellation = Context.System.Scheduler
                    .ScheduleTellRepeatedlyCancelable(AutoUpdateInterval, AutoUpdateInterval, Self, new ScheduledUpdate(AutoUpdateReplayMax), Self);
            }
        }

        private void StartRecovery(Recovery recovery)
        {
            ChangeState(RecoveryStarted(recovery.ReplayMax));
            LoadSnapshot(SnapshotterId, recovery.FromSnapshot, recovery.ToSequenceNr);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            _currentState.StateReceive(receive, message);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void AroundPreStart()
        {
            // Fail fast on missing plugins.
            var j = Journal;
            var s = SnapshotStore;
            base.AroundPreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
        protected override void PreRestart(Exception reason, object message)
        {
            try
            {
                _internalStash.UnstashAll();
            }
            finally
            {
                base.PreRestart(reason, message);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            if (_scheduleCancellation != null)
            {
                _scheduleCancellation.Cancel();
                _scheduleCancellation = null;
            }
            base.PostStop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void Unhandled(object message)
        {
            if (message is RecoveryCompleted) return; // ignore
            base.Unhandled(message);
        }
    }
}
