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
            if (_scheduleCancelation != null) _scheduleCancelation.Cancel();
            base.PostStop();
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            _currentState.StateReceive(receive, message);
            return true;
        }

        protected override void Unhandled(object message)
        {
            if (message is RecoveryCompleted) ; // ignore
            else if (message is RecoveryFailure)
            {
                var fail = (RecoveryFailure)message;
                var errorMessage = string.Format("Persistent view killed after the recovery failure (Persistence id: {0}). To avoid killing persistent actors on recovery failures, PersistentView must handle RecoveryFailure messages. Failure was caused by: {1}", PersistenceId, fail.Cause.Message);

                throw new ActorKilledException(errorMessage);
            }
            else base.Unhandled(message);
        }
    }
}