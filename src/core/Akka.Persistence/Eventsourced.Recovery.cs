﻿//-----------------------------------------------------------------------
// <copyright file="Eventsourced.Recovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="receive">TBD</param>
    /// <param name="message">TBD</param>
    internal delegate void StateReceive(Receive receive, object message);

    /// <summary>
    /// TBD
    /// </summary>
    internal class EventsourcedState
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="isRecoveryRunning">TBD</param>
        /// <param name="stateReceive">TBD</param>
        public EventsourcedState(string name, bool isRecoveryRunning, StateReceive stateReceive)
        {
            Name = name;
            IsRecoveryRunning = isRecoveryRunning;
            StateReceive = stateReceive;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsRecoveryRunning { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public StateReceive StateReceive { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class Eventsourced
    {
        /// <summary>
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered with a <see cref="SnapshotOffer"/> 
        /// message to the actor's <see cref="ReceiveRecover"/>. Then initiates a message replay, either starting 
        /// from the loaded snapshot or from scratch, and switches to <see cref="ReplayStarted"/> state. 
        /// All incoming messages are stashed.
        /// </summary>
        /// <param name="maxReplays">Maximum number of messages to replay</param>
        private EventsourcedState RecoveryStarted(long maxReplays)
        {
            // protect against snapshot stalling forever because journal overloaded and such
            var timeout = Extension.JournalConfigFor(JournalPluginId).GetTimeSpan("recovery-event-timeout", null, false);
            var timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, new RecoveryTick(true), Self);

            Receive recoveryBehavior = message =>
            {
                Receive receiveRecover = ReceiveRecover;
                if (message is IPersistentRepresentation && IsRecovering)
                    return receiveRecover((message as IPersistentRepresentation).Payload);
                else if (message is SnapshotOffer)
                    return receiveRecover((SnapshotOffer)message);
                else if (message is RecoveryCompleted)
                    return receiveRecover(RecoveryCompleted.Instance);
                else return false;
            };

            return new EventsourcedState("recovery started - replay max: " + maxReplays, true, (receive, message) =>
            {
                if (message is LoadSnapshotResult)
                {
                    var res = (LoadSnapshotResult)message;
                    timeoutCancelable.Cancel();
                    if (res.Snapshot != null)
                    {
                        var snapshot = res.Snapshot;
                        LastSequenceNr = snapshot.Metadata.SequenceNr;
                        // Since we are recovering we can ignore the receive behavior from the stack
                        base.AroundReceive(recoveryBehavior, new SnapshotOffer(snapshot.Metadata, snapshot.Snapshot));
                    }

                    ChangeState(Recovering(recoveryBehavior, timeout));
                    Journal.Tell(new ReplayMessages(LastSequenceNr + 1L, res.ToSequenceNr, maxReplays, PersistenceId, Self));
                }
                else if (message is RecoveryTick)
                {
                    try
                    {
                        OnRecoveryFailure(
                            new RecoveryTimedOutException(
                                $"Recovery timed out, didn't get snapshot within {timeout.TotalSeconds}s."));
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
                else
                    StashInternally(message);
            });
        }

        /// <summary>
        /// Processes replayed messages, if any. The actor's <see cref="ReceiveRecover"/> is invoked with the replayed events.
        /// 
        /// If replay succeeds it got highest stored sequence number response from the journal and then switches
        /// to <see cref="ProcessingCommands"/> state.
        /// If replay succeeds the <see cref="OnReplaySuccess"/> callback method is called, otherwise
        /// <see cref="OnRecoveryFailure"/>.
        /// 
        /// All incoming messages are stashed.
        /// </summary>
        private EventsourcedState Recovering(Receive recoveryBehavior, TimeSpan timeout)
        {
            // protect against event replay stalling forever because of journal overloaded and such
            var timeoutCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(timeout, timeout, Self,
                new RecoveryTick(false), Self);
            var eventSeenInInterval = false;

            return new EventsourcedState("replay started", true, (receive, message) =>
            {
                if (message is ReplayedMessage)
                {
                    var m = (ReplayedMessage)message;
                    try
                    {
                        eventSeenInInterval = true;
                        UpdateLastSequenceNr(m.Persistent);
                        base.AroundReceive(recoveryBehavior, m.Persistent);
                    }
                    catch (Exception cause)
                    {
                        timeoutCancelable.Cancel();
                        try
                        {
                            OnRecoveryFailure(cause, m.Persistent.Payload);
                        }
                        finally
                        {
                            Context.Stop(Self);
                        }
                    }
                }
                else if (message is RecoverySuccess)
                {
                    var m = (RecoverySuccess) message;
                    timeoutCancelable.Cancel();
                    OnReplaySuccess();
                    ChangeState(ProcessingCommands());
                    _sequenceNr = m.HighestSequenceNr;
                    LastSequenceNr = m.HighestSequenceNr;
                    _internalStash.UnstashAll();
                    base.AroundReceive(recoveryBehavior, RecoveryCompleted.Instance);
                }
                else if (message is ReplayMessagesFailure)
                {
                    var failure = (ReplayMessagesFailure)message;
                    timeoutCancelable.Cancel();
                    try
                    {
                        OnRecoveryFailure(failure.Cause, message: null);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
                else if (message is RecoveryTick)
                {
                    var isSnapshotTick = ((RecoveryTick) message).Snapshot;
                    if (!isSnapshotTick)
                    {
                        if (!eventSeenInInterval)
                        {
                            timeoutCancelable.Cancel();
                            try
                            {
                                OnRecoveryFailure(
                                    new RecoveryTimedOutException(
                                        $"Recovery timed out, didn't get event within {timeout.TotalSeconds}s, highest sequence number seen {_sequenceNr}."));
                            }
                            finally
                            {
                                Context.Stop(Self);
                            }
                        }
                        else
                        {
                            eventSeenInInterval = false;
                        }
                    }
                    else
                    {
                        // snapshot tick, ignore
                    }
                }
                else
                    StashInternally(message);
            });
        }

        /// <summary>
        /// If event persistence is pending after processing a command, event persistence 
        /// is triggered and the state changes to <see cref="PersistingEvents"/>.
        /// </summary>
        private EventsourcedState ProcessingCommands()
        {
            return new EventsourcedState("processing commands", false, (receive, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, err =>
                {
                    _pendingInvocations.Pop();
                    UnstashInternally(err);
                });
                if (!handled)
                {
                    try
                    {
                        base.AroundReceive(receive, message);
                        OnProcessingCommandsAroundReceiveComplete(false);
                    }
                    catch (Exception)
                    {
                        OnProcessingCommandsAroundReceiveComplete(true);
                        throw;
                    }
                }
            });
        }

        private void OnProcessingCommandsAroundReceiveComplete(bool err)
        {
            if (_eventBatch.Count > 0) FlushBatch();

            if (_pendingStashingPersistInvocations > 0)
                ChangeState(PersistingEvents());
            else
                UnstashInternally(err);
        }

        private void FlushBatch()
        {
            if (_eventBatch.Count > 0)
            {
                foreach (var p in _eventBatch.Reverse())
                {
                    _journalBatch.Add(p);
                }
                _eventBatch = new LinkedList<IPersistentEnvelope>();
            }

            FlushJournalBatch();
        }

        /// <summary>
        /// Remains until pending events are persisted and then changes state to <see cref="ProcessingCommands"/>.
        /// Only events to be persisted are processed. All other messages are stashed internally.
        /// </summary>
        private EventsourcedState PersistingEvents()
        {
            return new EventsourcedState("persisting events", false, (receive, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, err =>
                {
                    var invocation = _pendingInvocations.Pop();

                    // enables an early return to `processingCommands`, because if this counter hits `0`,
                    // we know the remaining pendingInvocations are all `persistAsync` created, which
                    // means we can go back to processing commands also - and these callbacks will be called as soon as possible
                    if (invocation is StashingHandlerInvocation)
                        _pendingStashingPersistInvocations--;

                    if (_pendingStashingPersistInvocations == 0)
                    {
                        ChangeState(ProcessingCommands());
                        UnstashInternally(err);
                    }
                });

                if (!handled)
                    StashInternally(message);
            });
        }

        private void PeekApplyHandler(object payload)
        {
            try
            {
                _pendingInvocations.First.Value.Handler(payload);
            }
            finally
            {
                FlushBatch();
            }
        }

        private bool CommonProcessingStateBehavior(object message, Action<bool> onWriteMessageComplete)
        {
            // _instanceId mismatch can happen for persistAsync and defer in case of actor restart
            // while message is in flight, in that case we ignore the call to the handler
            if (message is WriteMessageSuccess)
            {
                var m = (WriteMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    UpdateLastSequenceNr(m.Persistent);
                    try
                    {
                        PeekApplyHandler(m.Persistent.Payload);
                        onWriteMessageComplete(false);
                    }
                    catch
                    {
                        onWriteMessageComplete(true);
                        throw;
                    }
                }
            }
            else if (message is WriteMessageRejected)
            {
                var m = (WriteMessageRejected)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    var p = m.Persistent;
                    UpdateLastSequenceNr(p);
                    onWriteMessageComplete(false);
                    OnPersistRejected(m.Cause, p.Payload, p.SequenceNr);
                }
            }
            else if (message is WriteMessageFailure)
            {
                var m = (WriteMessageFailure)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    var p = m.Persistent;
                    onWriteMessageComplete(false);
                    try
                    {
                        OnPersistFailure(m.Cause, p.Payload, p.SequenceNr);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                }
            }
            else if (message is LoopMessageSuccess)
            {
                var m = (LoopMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    try
                    {
                        PeekApplyHandler(m.Message);
                        onWriteMessageComplete(false);
                    }
                    catch (Exception)
                    {
                        onWriteMessageComplete(true);
                        throw;
                    }
                }
            }
            else if (message is WriteMessagesSuccessful)
            {
                _isWriteInProgress = false;
                FlushJournalBatch();
            }
            else if (message is WriteMessagesFailed)
            {
                _isWriteInProgress = false;
                // it will be stopped by the first WriteMessageFailure message
            }
            else if (message is RecoveryTick)
            {
                // we may have one of these in the mailbox before the scheduled timeout
                // is cancelled when recovery has completed, just consume it so the concrete actor never sees it
            }
            else return false;
            return true;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class LinkedListExtensions
    {
        /// <summary>
        /// Removes first element from the list and returns it or returns default value if list was empty.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        internal static T Pop<T>(this LinkedList<T> self)
        {
            if (self.First != null)
            {
                var first = self.First.Value;
                self.RemoveFirst();
                return first;
            }

            return default(T);
        }
    }
}

