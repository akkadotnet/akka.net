using System;
using Akka.Actor;

namespace Akka.Persistence
{

    internal interface IState
    {
        void AroundReceive(Receive receive, object message);
    }

    internal sealed class RecoveryContext
    {
        public IState CurrentState { get; set; }
        public Action<object> InternalUnhandled { get; set; }
        public string SnapshoterId { get; set; }
        public IStash Stash { get; set; }

        public void WithCurrentPersistent(IPersistentRepresentation persistent, Action<Receive, object> process)
        {
            throw new NotImplementedException();
        }

        public void LoadSnapshot(string snapshoterId, SnapshotSelectionCriteria fromSnapshot, long toSequenceNr)
        {
            throw new NotImplementedException();
        }
    }

    internal abstract class State : IState
    {
        protected readonly RecoveryContext Context;
        protected Exception RecoveryFailureCause;
        protected object RecoveryFailureMessage;

        protected State(RecoveryContext context)
        {
            Context = context;
        }

        public abstract void AroundReceive(Receive receive, object message);

        protected void Process(Receive receive, object message)
        {
            if (!receive(message))
            {
                Context.InternalUnhandled(message);
            }
        }

        protected void ProcessPersistent(Receive receive, IPersistentRepresentation persistent)
        {
            Context.WithCurrentPersistent(persistent, Process);
        }

        protected void RecordFailure(Exception cause, object message)
        {
            RecoveryFailureCause = cause;
            RecoveryFailureMessage = message;
        }
    }


    internal class RecoveryState : State
    {
        public RecoveryState(RecoveryContext context)
            : base(context)
        {
        }

        public override void AroundReceive(Receive receive, object message)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Class representing initial state, waiting for <see cref="Recover"/> request, 
    /// submitting a <see cref="LoadSnapshot"/> request to the snapshot store 
    /// and changing to <see cref="RecoveryStartedState"/>.
    /// </summary>
    internal class RecoveryPendingState : State
    {
        public RecoveryPendingState(RecoveryContext context)
            : base(context)
        {
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if (message is Recover)
            {
                var msg = (Recover)message;
                Context.CurrentState = new RecoveryStartedState(Context, msg.ReplayMax);
                Context.LoadSnapshot(Context.SnapshoterId, msg.FromSnapshot, msg.ToSequenceNr);
            }
            else
            {
                Context.Stash.Stash();
            }
        }

        public override string ToString()
        {
            return "recovery pending";
        }
    }

    /// <summary>
    /// Class representing state responsible for processing a loaded snapshot. Snapshot is offered with 
    /// <see cref="SnapshotOffer"/> message to current <see cref="PersistentActorBase"/> behavior. Then 
    /// a message replay is initialized, either starting from loaded snapshot or from scratch. 
    /// In the end it switches to <see cref="ReplayStartedState"/>.
    /// </summary>
    internal class RecoveryStartedState : State
    {
        private readonly long _replayMax;

        public RecoveryStartedState(RecoveryContext context, long replayMax)
            : base(context)
        {
            _replayMax = replayMax;
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if(message is Recover) return;
            if (message is LoadSnapshotResult)
            {
                var msg = (LoadSnapshotResult) message;
                if (msg.Snapshot.HasValue)
                {
                    var selectedSnapshot = msg.Snapshot.Value;
                    Actor.UpdateLastSequenceNr(selectedSnapshot.Metadata.SequenceNr);
                    Process(receive, new SnapshotOffer(selectedSnapshot.Metadata, selectedSnapshot.Snapshot));
                }

                Actor.CurrentState = new ReplayStartedState(Actor, true);
                var replayMessages = new ReplayMessages(Actor.LastSequenceNr + 1, 
                    msg.ToSequenceNr, 
                    _replayMax,
                    Actor.PersistenceId, 
                    ActorCell.GetCurrentSelfOrNoSender());
                Actor.Journal.Tell(replayMessages);
            }
            else
            {
                Actor.Stash.Stash();
            }
        }

        public override string ToString()
        {
            return "recovery started - replay max: " + _replayMax;
        }
    }

    /// <summary>
    /// Class representing state, which processes replayed messages. Current actor behavior is invoked 
    /// with replayed persistent messages. If message replaying fails, an exception is caught and stored 
    /// for later handling in <see cref="ReplayFailedState"/>, which occurs immediately.
    /// </summary>
    internal class ReplayStartedState : State
    {
        private readonly bool _shouldAwait;

        /// <param name="shouldAwait">
        /// If true actor behavior is defered until replay completes. Otherwise, actor behavior is called immediately on replayed messages.
        /// </param>
        public ReplayStartedState(RecoveryContext context, bool shouldAwait = false)
            : base(context)
        {
            _shouldAwait = shouldAwait;
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if (message is Recover) return;
            if (message is ReplayedMessage)
            {
                var msg = (ReplayedMessage) message;
                try
                {
                    ProcessPersistent(receive, msg.Persistent);
                }
                catch (Exception cause)
                {
                    Actor.CurrentState = new ReplayFailedState(Actor);
                    RecordFailure(cause, message);
                }
            }
            else if (message is ReplayMessagesSuccess)
            {
                Actor.OnReplaySuccess(receive, _shouldAwait);
            }
            else if (message is ReplayMessagesFailure)
            {
                var msg = (ReplayMessagesFailure) message;
                Actor.OnReplayFailure(receive, _shouldAwait, msg.Cause);
            }
            else
            {
                if (_shouldAwait)
                {
                    Actor.Stash.Stash();
                }
                else
                {
                    Process(receive, message);
                }
            }
        }
    }

    /// <summary>
    /// Class representing recovery state, in which actor consumes remaining replayed messages and then 
    /// changes to <see cref="PrepareRestartState"/>. Message that caused a failure, is re-added to 
    /// mailbox and re-received during <see cref="PrepareRestartState"/>.
    /// </summary>
    internal class ReplayFailedState : State
    {
        public ReplayFailedState(RecoveryContext context)
            : base(context)
        {
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if (message is ReplayMessagesFailure)
            {
                ReplayCompleted();
                Actor.UpdateLastSequenceNr(long.MaxValue);
            }
            else if (message is ReplayMessagesSuccess)
            {
                ReplayCompleted();
            }
            else if (message is ReplayedMessage)
            {
                var msg = (ReplayedMessage) message;
                Actor.UpdateLastSequenceNr(msg.Persistent);
            }
            else if (message is Recover)
            {
            }
            else
            {
                Actor.Stash.Stash();
            }
        }

        private void ReplayCompleted()
        {
            Actor.CurrentState = new PrepareRestartState(Actor);
            // mailbox.enqueueFirst(self, _recoveryFailureMessage)
        }

        public override string ToString()
        {
            return "replay failed";
        }
    }

    /// <summary>
    /// Re-receives the replayed message that caused an exception and re-throws that exception.
    /// </summary>
    internal class PrepareRestartState : State
    {
        public PrepareRestartState(RecoveryContext context)
            : base(context)
        {
        }

        public override void AroundReceive(Receive receive, object message)
        {
            if (message is ReplayedMessage)
            {
                throw RecoveryFailureCause;
            }
        }

        public override string ToString()
        {
            return "prepare restart";
        }
    }
}