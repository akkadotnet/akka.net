//-----------------------------------------------------------------------
// <copyright file="PersistentView.Recovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered with a <see cref="SnapshotOffer"/>
        /// message to the actor's <see cref="PersistentView.Receive"/> method. Then initiates a message replay, either 
        /// starting from the loaded snapshot or from scratch, and switches to <see cref="ReplayStarted"/> state.
        /// All incoming messages are stashed.
        /// </summary>
        private ViewState RecoveryStarted(long replayMax)
        {
            return new ViewState("recovery started - replayMax: " + replayMax, true, (receive, message) =>
            {
                if (message is LoadSnapshotResult)
                {
                    var loadResult = (LoadSnapshotResult)message;
                    if (loadResult.Snapshot != null)
                    {
                        var selectedSnapshot = loadResult.Snapshot;
                        LastSequenceNr = selectedSnapshot.Metadata.SequenceNr;
                        base.AroundReceive(receive, new SnapshotOffer(selectedSnapshot.Metadata, selectedSnapshot.Snapshot));
                    }
                    ChangeState(ReplayStarted(true));
                    Journal.Tell(new ReplayMessages(LastSequenceNr + 1, loadResult.ToSequenceNr, replayMax, PersistenceId, Self));
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Processes replayed message, if any. The actor's <see cref="PersistentView.Receive"/> is invoked 
        /// with the replayed events.
        /// 
        /// If replay succeeds it got highest stored sequence number response from the journal and
        /// then switche it switches to <see cref="Idle"/> state.
        /// 
        /// 
        /// If replay succeeds the <see cref="OnReplaySuccess"/> callback method is called, otherwise
        /// <see cref="OnReplayError"/> is called and remaining replay events are consumed (ignored).
        /// 
        /// All incoming messages are stashed when <paramref name="shouldAwait"/> is true.
        /// </summary>
        private ViewState ReplayStarted(bool shouldAwait)
        {
            return new ViewState("replay started", true, (receive, message) =>
            {
                if (message is ReplayedMessage)
                {
                    var replayedMessage = (ReplayedMessage)message;
                    try
                    {
                        UpdateLastSequenceNr(replayedMessage.Persistent);
                        base.AroundReceive(receive, replayedMessage.Persistent.Payload);
                    }
                    catch (Exception ex)
                    {
                        ChangeState(IgnoreRemainingReplay(ex));
                    }
                }
                else if (message is RecoverySuccess)
                {
                    OnReplayComplete();
                }
                else if (message is ReplayMessagesFailure)
                {
                    var replayFailureMessage = (ReplayMessagesFailure)message;
                    try
                    {
                        OnReplayError(replayFailureMessage.Cause);
                    }
                    finally
                    {
                        OnReplayComplete();
                    }
                }
                else if (message is ScheduledUpdate)
                {
                    // ignore
                }
                else if (message is Update)
                {
                    var u = (Update) message;
                    if (u.IsAwait)
                    {
                        _internalStash.Stash();
                    }
                }
                else
                {
                    if (shouldAwait)
                    {
                        _internalStash.Stash();
                    }
                    else
                    {
                        try
                        {
                            base.AroundReceive(receive, message);
                        }
                        catch (Exception ex)
                        {
                            ChangeState(IgnoreRemainingReplay(ex));
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Switches to <see cref="Idle"/>.
        /// </summary>
        private void OnReplayComplete()
        {
            ChangeState(Idle());

            _internalStash.UnstashAll();
        }

        /// <summary>
        /// Consumes remaining replayed messages and then throws the exception.
        /// </summary>
        private ViewState IgnoreRemainingReplay(Exception cause)
        {
            return new ViewState("replay failed", true, (receive, message) =>
            {
                if (message is ReplayedMessage) { } 
                else if (message is ReplayMessagesFailure)
                {
                    // journal couldn't tell the maximum stored sequence number, hence the next
                    // replay must be a full replay (up to the highest stored sequence number)
                    // Recover(lastSequenceNr) is sent by preRestart
                    LastSequenceNr = long.MaxValue;
                    OnReplayFailureCompleted(cause);
                }
                else if (message is RecoverySuccess) OnReplayFailureCompleted(cause);
                else _internalStash.Stash();
            });
        }

        private void OnReplayFailureCompleted( Exception cause)
        {
            ChangeState(Idle());
            _internalStash.UnstashAll();
            throw cause;
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
                if (message is ReplayedMessage)
                {
                    // we can get ReplayedMessage here if it was stashed by user during replay
                    // unwrap the payload
                    base.AroundReceive(receive, ((ReplayedMessage) message).Persistent.Payload);
                }
                else if (message is ScheduledUpdate)
                {
                    var scheduled = (ScheduledUpdate) message;
                    ChangeStateToReplayStarted(false, scheduled.ReplayMax);
                }
                else if (message is Update)
                {
                    var update = (Update)message;
                    ChangeStateToReplayStarted(update.IsAwait, update.ReplayMax);
                }
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

