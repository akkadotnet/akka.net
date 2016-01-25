//-----------------------------------------------------------------------
// <copyright file="PersistentView.Lifecycle.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{
    public partial class PersistentView
    {
        protected override void PreStart()
        {
            base.PreStart();
            Self.Tell(new Recover(SnapshotSelectionCriteria.Latest, replayMax: AutoUpdateReplayMax));

            if (IsAutoUpdate)
            {
                _scheduleCancellation = Context.System.Scheduler
                    .ScheduleTellRepeatedlyCancelable(AutoUpdateInterval, AutoUpdateInterval, Self, new ScheduledUpdate(AutoUpdateReplayMax), Self);
            }
        }

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

        protected override void PostStop()
        {
            if (_scheduleCancellation != null)
            {
                _scheduleCancellation.Cancel();
                _scheduleCancellation = null;
            }
            base.PostStop();
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            _currentState.StateReceive(receive, message);
            return true;
        }

        protected override void Unhandled(object message)
        {
            if (message is RecoveryCompleted) return; // ignore
            if (message is RecoveryFailure)
            {
                var fail = (RecoveryFailure)message;
                var errorMessage = string.Format("Persistent view killed after the recovery failure (Persistence id: {0}). To avoid killing persistent actors on recovery failures, PersistentView must handle RecoveryFailure messages. Failure was caused by: {1}", PersistenceId, fail.Cause.Message);

                throw new ActorKilledException(errorMessage);
            }
            base.Unhandled(message);
        }
    }
}

