//-----------------------------------------------------------------------
// <copyright file="PersistentView.Recovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Persistence
{
    //TODO: There are some duplication of the recovery state management here and in EventsourcedState,
    //      but the enhanced PersistentView will not be based on recovery infrastructure, and
    //      therefore this code will be replaced anyway

    internal class ViewState
    {
        public ViewState(string name, bool isRecoveryRunning, StateReceive stateReceive)
        {
            Name = name;
            StateReceive = stateReceive;
            IsRecoveryRunning = isRecoveryRunning;
        }

        public string Name { get; private set; }
        public bool IsRecoveryRunning { get; private set; }
        public StateReceive StateReceive { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public partial class PersistentView
    {
        /// <summary>
        /// Initial state. Waits for <see cref="Recover"/> request, and then submits a <see cref="LoadSnapshot"/>
        /// request to snapshot store and then changes to <see cref="RecoveryStarted"/> state. All other message types are stashed.
        /// </summary>
        private ViewState RecoveryPending()
        {
            return new ViewState("recovery pending", true, (receive, message) =>
            {
                if (message is Recover)
                {
                    var recover = (Recover)message;
                    ChangeState(RecoveryStarted(recover.ReplayMax));
                    LoadSnapshot(SnapshotterId, recover.FromSnapshot, recover.ToSequenceNr);
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered to view via <see cref="SnapshotOffer"/>
        /// message in <see cref="PersistentActor.ReceiveRecover"/> method. Then initiates a message replay, either 
        /// starting from the loaded snapshot or from scratch. Then switches to <see cref="ReplayStarted"/> state.
        /// 
        /// All incoming messages are stashed.
        /// </summary>
        private ViewState RecoveryStarted(long replayMax)
        {
            return new ViewState("recovery started - replayMax: " + replayMax, true, (receive, message) =>
            {
                if (message is Recover) ; // ignore
                else if (message is LoadSnapshotResult)
                {
                    var loadResult = (LoadSnapshotResult)message;
                    if (loadResult.Snapshot != null)
                    {
                        var selectedSnapshot = loadResult.Snapshot;
                        LastSequenceNr = selectedSnapshot.Metadata.SequenceNr;
                        // since we're recovering, we can ignore receive behavior from the stack
                        base.AroundReceive(receive, new SnapshotOffer(selectedSnapshot.Metadata, selectedSnapshot.Snapshot));
                    }
                    ChangeState(ReplayStarted(true));
                    Journal.Tell(new ReplayMessages(LastSequenceNr + 1, loadResult.ToSequenceNr, replayMax, PersistenceId, Self));
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Processes replayed message, if any. The actor's <see cref="PersistentActor.ReceiveRecover"/> is invoked 
        /// with the replayed events.
        /// 
        /// If replay succeeds it switches to <see cref="Initialized"/> state and requests the highest stored sequence
        /// number from the journal and <see cref="OnReplaySuccess"/> is called. Otherwise the <see cref="RecoveryFailure"/> 
        /// is emitted with <see cref="OnReplayFailure"/> being called.
        /// 
        /// If processing fails, the exception is caught and stored for being thrown later and the state is changed
        /// to <see cref="RecoveryFailed"/>.
        /// 
        /// All incoming messages are stashed.
        /// </summary>
        private ViewState ReplayStarted(bool shouldAwait)
        {
            var stashUpdate = shouldAwait;
            return new ViewState("replay started", true, (receive, message) =>
            {
                if (message is Update)
                {
                    var u = (Update)message;
                    if (u.IsAwait && !stashUpdate)
                    {
                        stashUpdate = true;
                        _internalStash.Stash();
                    }
                }
                else if (message is Recover) ; // ignore
                else if (message is ReplayedMessage)
                {
                    var replayedMessage = (ReplayedMessage)message;
                    try
                    {
                        UpdateLastSequenceNr(replayedMessage.Persistent);
                        base.AroundReceive(receive, replayedMessage.Persistent.Payload);
                    }
                    catch (Exception exc)
                    {
                        var currentMessage = Context.AsInstanceOf<ActorCell>().CurrentMessage;
                        // delay throwing exception to prepare restart
                        ChangeState(ReplayFailed(exc, currentMessage));
                    }
                }
                else if (message is ReplayMessagesSuccess)
                {
                    OnReplayComplete(shouldAwait);
                }
                else if (message is ReplayMessagesFailure)
                {
                    var replayFailureMessage = (ReplayMessagesFailure)message;
                    OnReplayComplete(shouldAwait);
                    // FIXME what happens if RecoveryFailure is handled, i.e. actor is not stopped?
                    base.AroundReceive(receive, new RecoveryFailure(replayFailureMessage.Cause));
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Switches to <see cref="Idle"/> state and schedules the next update if <see cref="IsAutoUpdate"/> flag is set.
        /// </summary>
        private void OnReplayComplete(bool shouldAwait)
        {
            ChangeState(Idle());

            if (shouldAwait)
            {
                _internalStash.UnstashAll();
            }
        }

        /// <summary>
        /// Consumes remaining replayed messages and switches to <see cref="PrepareRestart"/> state. Message that
        /// caused an exception during replay is re-added to the mailbox and re-received in <see cref="PrepareRestart"/> state.
        /// </summary>
        private ViewState ReplayFailed(Exception cause, object failedMessage)
        {
            return new ViewState("replay failed", true, (receive, message) =>
            {
                if (message is ReplayMessagesFailure)
                {
                    OnReplayFailureCompleted(receive, cause, failedMessage as IPersistentRepresentation);
                    // journal couldn't tell the maximum stored sequence number, hence the next
                    // replay must be a full replay (up to the highest stored sequence number)
                    // Recover(lastSequenceNr) is sent by preRestart
                    LastSequenceNr = long.MaxValue;
                }
                else if (message is ReplayMessagesSuccess) OnReplayFailureCompleted(receive, cause, failedMessage as IPersistentRepresentation);
                else if (message is ReplayedMessage) UpdateLastSequenceNr(((ReplayedMessage)message).Persistent);
                else if (message is Recover) ; // ignore
                else _internalStash.Stash();
            });
        }

        private void OnReplayFailureCompleted(Receive receive, Exception cause, IPersistentRepresentation failed)
        {
            ChangeState(Idle());
            var recoveryFailure = failed != null ? new RecoveryFailure(cause, failed.SequenceNr, failed.Payload) : new RecoveryFailure(cause);
            base.AroundReceive(receive, recoveryFailure);
        }


        /// <summary>
        /// When receiving an <see cref="Update"/> event, switches to <see cref="ReplayStarted"/> state
        /// and triggers an incremental message replay. For any other message invokes actor default behavior.
        /// </summary>
        /// <returns></returns>
        private ViewState Idle()
        {
            return new ViewState("idle", false, (receive, message) =>
            {
                if (message is Update)
                {
                    var update = (Update)message;
                    ChangeStateToReplayStarted(update.IsAwait, update.ReplayMax);
                }
                else if (message is ScheduledUpdate)
                {
                    var scheduled = (ScheduledUpdate) message;
                    ChangeStateToReplayStarted(false, scheduled.ReplayMax);
                }
                else if (message is Recover) ; // ignore
                else base.AroundReceive(receive, message);
            });
        }

        private void ChangeStateToReplayStarted(bool isAwait, long replayMax)
        {
            ChangeState(ReplayStarted(isAwait));
            Journal.Tell(new ReplayMessages(LastSequenceNr + 1, long.MaxValue, replayMax, PersistenceId, Self));
        }
    }
}

