using System;
using Akka.Actor;

namespace Akka.Persistence
{
    public partial class Eventsourced
    {
        protected override void PreStart()
        {
            Self.Tell(new Recover(SnapshotSelectionCriteria.Latest));
        }

        protected override void PreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);

            Self.Tell(message != null
                ? new Recover(SnapshotSelectionCriteria.Latest, toSequenceNr: LastSequenceNr)
                : new Recover(SnapshotSelectionCriteria.Latest));
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
                var msg = string.Format("{0} was killed after recovery failure (persistence id = {1}). To avoid killing persistent actors on recovery failure, a PersistentActor must handle RecoveryFailure messages. Recovery failure was caused by: {2}", 
                    GetType().Name, PersistenceId, (message as RecoveryFailure).Cause.Message);

                throw new ActorKilledException(msg);
            }
            else if (message is PersistenceFailure)
            {
                var fail = message as PersistenceFailure;
                var msg = string.Format("{0} was killed after persistence failure (persistence id = {1}, sequence nr: {2}, payload type: {3}). To avoid killing persistent actors on recovery failure, a PersistentActor must handle RecoveryFailure messages. Persistence failure was caused by: {4}",
                    GetType().Name, PersistenceId, fail.SequenceNr, fail.Payload.GetType().Name, fail.Cause.Message);
                throw new ActorKilledException(msg);
            }
            else base.Unhandled(message);
        }
    }
}