//-----------------------------------------------------------------------
// <copyright file="Eventsourced.Recovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Internal;

namespace Akka.Persistence
{
    internal delegate void StateReceive(Receive receive, object message);

    internal class EventsourcedState
    {
        public EventsourcedState(string name, Func<bool> isRecoveryRunning, StateReceive stateReceive)
        {
            Name = name;
            IsRecoveryRunning = isRecoveryRunning;
            StateReceive = stateReceive;
        }

        public string Name { get; }
        public Func<bool> IsRecoveryRunning { get; }
        public StateReceive StateReceive { get; }

        public override string ToString() => Name;
    }
    
    public abstract partial class Eventsourced
    {
        private ICancelable? _timeoutCancelable;
        
        /// <summary>
        /// Initial state. Before starting the actual recovery it must get a permit from the `RecoveryPermitter`.
        /// When starting many persistent actors at the same time the journal and its data store is protected from
        /// being overloaded by limiting number of recoveries that can be in progress at the same time.
        /// When receiving `RecoveryPermitGranted` it switches to `recoveryStarted` state.
        /// All incoming messages are stashed.
        /// </summary>
        private EventsourcedState WaitingRecoveryPermit(Recovery recovery)
        {
            return new EventsourcedState("waiting for recovery permit", () => true, (_, message) =>
            {
                if (message is RecoveryPermitGranted)
                    StartRecovery(recovery);
                else
                    StashInternally(message);
            });
        }

        /// <summary>
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered with a <see cref="SnapshotOffer"/>
        /// message to the actor's <see cref="ReceiveRecover"/>. Then initiates a message replay, either starting
        /// from the loaded snapshot or from scratch, and switches to <see cref="RecoveryStarted"/> state.
        /// All incoming messages are stashed.
        /// </summary>
        /// <param name="maxReplays">Maximum number of messages to replay</param>
        private EventsourcedState RecoveryStarted(long maxReplays)
        {
            // protect against snapshot stalling forever because journal overloaded and such
            var timeout = Extension.JournalConfigFor(JournalPluginId).GetTimeSpan("recovery-event-timeout", null, false);
            _timeoutCancelable?.Cancel();
            _timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, new RecoveryTick(true), Self);
            
            var snapshotIsOptional = Extension.SnapshotStoreConfigFor(SnapshotPluginId).GetBoolean("snapshot-is-optional", false);
            
            bool RecoveryBehavior(object message)
            {
                Receive receiveRecover = ReceiveRecover;
                switch (message)
                {
                    case IPersistentRepresentation representation when IsRecovering:
                        return receiveRecover(representation.Payload);
                    case SnapshotOffer offer:
                        return receiveRecover(offer);
                    case RecoveryCompleted _:
                        return receiveRecover(RecoveryCompleted.Instance);
                    default:
                        return false;
                }
            }

            return new EventsourcedState("recovery started - replay max: " + maxReplays, () => true, (_, message) =>
            {
                try
                {
                    switch (message)
                    {
                        case LoadSnapshotResult res:
                        {
                            _timeoutCancelable?.Cancel();
                            _timeoutCancelable = null;
                            if (res.Snapshot != null)
                            {
                                var offer = new SnapshotOffer(res.Snapshot.Metadata, res.Snapshot.Snapshot);
                                var seqNr = LastSequenceNr;
                                try
                                {
                                    LastSequenceNr = res.Snapshot.Metadata.SequenceNr;
                                    if (!base.AroundReceive(RecoveryBehavior, offer))
                                    {
                                        LastSequenceNr = seqNr;
                                        Unhandled(offer);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    try
                                    {
                                        OnRecoveryFailure(ex);
                                    }
                                    finally
                                    {
                                        Context.Stop(Self);
                                    }
                                    ReturnRecoveryPermit();
                                }
                            }

                            ChangeState(Recovering(RecoveryBehavior, timeout));
                            Journal.Tell(new ReplayMessages(LastSequenceNr + 1L, res.ToSequenceNr, maxReplays, PersistenceId, Self));
                            break;
                        }
                        case LoadSnapshotFailed failed:
                            _timeoutCancelable?.Cancel();
                            _timeoutCancelable = null;
                            if (snapshotIsOptional)
                            {
                                Log.Info("Snapshot load error for persistenceId [{0}]. Replaying all events since snapshot-is-optional=true", PersistenceId);
                                ChangeState(Recovering(RecoveryBehavior, timeout));
                                Journal.Tell(new ReplayMessages(LastSequenceNr +1L, long.MaxValue, maxReplays, PersistenceId, Self));
                            }
                            else 
                            {
                                try
                                {
                                    OnRecoveryFailure(failed.Cause);
                                }
                                finally
                                {
                                    Context.Stop(Self);
                                }
                                ReturnRecoveryPermit();
                            }
                            break;
                        case RecoveryTick { Snapshot: true }:
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
                            ReturnRecoveryPermit();
                            break;
                        default:
                            StashInternally(message);
                            break;
                    }
                }
                catch (Exception)
                {
                    _timeoutCancelable?.Cancel();
                    _timeoutCancelable = null;
                    ReturnRecoveryPermit();
                    throw;
                }
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
            _timeoutCancelable?.Cancel();
            _timeoutCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(timeout, timeout, Self, new RecoveryTick(false), Self);
            var eventSeenInInterval = false;
            var recoveryRunning = true;

            return new EventsourcedState("replay started", () => recoveryRunning, (_, message) =>
            {
                try
                {
                    switch (message)
                    {
                        case ReplayedMessage replayed:
                            try
                            {
                                eventSeenInInterval = true;
                                UpdateLastSequenceNr(replayed.Persistent);
                                base.AroundReceive(recoveryBehavior, replayed.Persistent);
                            }
                            catch (Exception cause)
                            {
                                _timeoutCancelable?.Cancel();
                                _timeoutCancelable = null;
                                try
                                {
                                    OnRecoveryFailure(cause, replayed.Persistent.Payload);
                                }
                                finally
                                {
                                    Context.Stop(Self);
                                }
                                ReturnRecoveryPermit();
                            }
                            break;
                        case RecoverySuccess success:
                            _timeoutCancelable?.Cancel();
                            _timeoutCancelable = null;
                            OnReplaySuccess();
                            var highestSeqNr = Math.Max(success.HighestSequenceNr, LastSequenceNr);
                            _sequenceNr = highestSeqNr;
                            LastSequenceNr = highestSeqNr;
                            recoveryRunning = false;
                            try
                            {
                                base.AroundReceive(recoveryBehavior, RecoveryCompleted.Instance);
                            }
                            finally
                            {
                                // in finally in case exception and resume strategy
                                TransitToProcessingState();
                            }
                            ReturnRecoveryPermit();
                            break;
                        case ReplayMessagesFailure failure:
                            _timeoutCancelable?.Cancel();
                            _timeoutCancelable = null;
                            try
                            {
                                OnRecoveryFailure(failure.Cause);
                            }
                            finally
                            {
                                Context.Stop(Self);
                            }
                            ReturnRecoveryPermit();
                            break;
                        case RecoveryTick { Snapshot: false }:
                            if (!eventSeenInInterval)
                            {
                                _timeoutCancelable?.Cancel();
                                _timeoutCancelable = null;
                                try
                                {
                                    OnRecoveryFailure(
                                        new RecoveryTimedOutException(
                                            $"Recovery timed out, didn't get event within {timeout.TotalSeconds}s, highest sequence number seen {LastSequenceNr}."));
                                }
                                finally
                                {
                                    Context.Stop(Self);
                                }
                                ReturnRecoveryPermit();
                            }
                            else
                            {
                                eventSeenInInterval = false;
                            }
                            break;
                        case RecoveryTick { Snapshot: true }:
                            // snapshot tick, ignore
                            break;
                        default:
                            StashInternally(message);
                            break;
                    }
                }
                catch (Exception)
                {
                    _timeoutCancelable?.Cancel();
                    _timeoutCancelable = null;
                    ReturnRecoveryPermit();
                    throw;
                }
            });
        }

        private void ReturnRecoveryPermit() =>
            RecoveryPermitter.Tell(Akka.Persistence.ReturnRecoveryPermit.Instance, Self);

        private void TransitToProcessingState()
        {
            if (_eventBatch.Count > 0) FlushBatch();

            if (_pendingStashingPersistInvocations > 0)
            {
                ChangeState(PersistingEvents());
            }
            else
            {
                ChangeState(ProcessingCommands());
                _internalStash.UnstashAll();
            }
        }

        /// <summary>
        /// Command processing state. If event persistence is pending after processing a command, event persistence
        /// is triggered and the state changes to <see cref="PersistingEvents"/>.
        /// </summary>
        private EventsourcedState ProcessingCommands()
        {
            return new EventsourcedState("processing commands", () => false, (receive, message) =>
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

            if (_asyncTaskRunning)
            {
                //do nothing, wait for the task to finish
            }
            else if (_pendingStashingPersistInvocations > 0)
                ChangeState(PersistingEvents());
            else
                UnstashInternally(err);
        }

        private void FlushBatch()
        {
            if (_eventBatch.Count > 0)
            {
                foreach (var p in _eventBatch)
                {
                    _journalBatch.Add(p);
                }
                _eventBatch.Clear();
            }

            FlushJournalBatch();
        }

        /// <summary>
        /// Event persisting state. Remains until pending events are persisted and then changes state to <see cref="ProcessingCommands"/>.
        /// Only events to be persisted are processed. All other messages are stashed internally.
        /// </summary>
        private EventsourcedState PersistingEvents()
        {
            return new EventsourcedState("persisting events", () => false, (_, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, err =>
                {
                    var invocation = _pendingInvocations.Pop();

                    // enables an early return to `processingCommands`, because if this counter hits `0`,
                    // we know the remaining pendingInvocations are all `persistAsync` created, which
                    // means we can go back to processing commands also - and these callbacks will be called as soon as possible
                    if (invocation is IStashingInvocation)
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

        /// <summary>
        /// Applies the handler for the first pending invocation.
        /// For sync handlers, invokes directly. For async handlers, uses RunTask.
        /// </summary>
        /// <param name="payload">The event payload to pass to the handler.</param>
        /// <param name="onComplete">Callback invoked when the handler completes (true if error).</param>
        private void PeekApplyHandler(object payload, Action<bool> onComplete)
        {
            var invocation = _pendingInvocations.First.Value;

            if (invocation is IAsyncHandlerInvocation asyncInv)
            {
                // Async handler - run via RunTask
                RunTask(async () =>
                {
                    try
                    {
                        await asyncInv.AsyncHandler(payload);
                        onComplete(false);
                    }
                    catch
                    {
                        onComplete(true);
                        throw;
                    }
                    finally
                    {
                        FlushBatch();
                    }
                });
            }
            else if (invocation is ISyncHandlerInvocation syncInv)
            {
                // Sync handler - invoke directly
                try
                {
                    syncInv.Handler(payload);
                    onComplete(false);
                }
                catch
                {
                    onComplete(true);
                    throw;
                }
                finally
                {
                    FlushBatch();
                }
            }
        }

        private bool CommonProcessingStateBehavior(object message, Action<bool> onWriteMessageComplete)
        {
            switch (message)
            {
                // _instanceId mismatch can happen for persistAsync and defer in case of actor restart
                // while message is in flight, in that case we ignore the call to the handler
                case WriteMessageSuccess m1:
                {
                    if (m1.ActorInstanceId == _instanceId)
                    {
                        UpdateLastSequenceNr(m1.Persistent);
                        PeekApplyHandler(m1.Persistent.Payload, onWriteMessageComplete);
                    }

                    break;
                }
                case WriteMessageRejected m2:
                {
                    if (m2.ActorInstanceId == _instanceId)
                    {
                        var p = m2.Persistent;
                        UpdateLastSequenceNr(p);
                        onWriteMessageComplete(false);
                        OnPersistRejected(m2.Cause, p.Payload, p.SequenceNr);
                    }

                    break;
                }
                case WriteMessageFailure m3:
                {
                    if (m3.ActorInstanceId == _instanceId)
                    {
                        var p = m3.Persistent;
                        onWriteMessageComplete(false);
                        try
                        {
                            OnPersistFailure(m3.Cause, p.Payload, p.SequenceNr);
                        }
                        finally
                        {
                            Context.Stop(Self);
                        }
                    }

                    break;
                }
                case LoopMessageSuccess m:
                {
                    if (m.ActorInstanceId == _instanceId)
                    {
                        PeekApplyHandler(m.Message, onWriteMessageComplete);
                    }

                    break;
                }
                case WriteMessagesSuccessful _:
                    _isWriteInProgress = false;
                    FlushJournalBatch();
                    break;
                case WriteMessagesFailed failed:
                    // if writeCount > 0 then WriteMessageFailure will follow that will stop the actor
                    if (failed.WriteCount == 0) _isWriteInProgress = false;
                    break;
                case RecoveryTick _:
                    // we may have one of these in the mailbox before the scheduled timeout
                    // is cancelled when recovery has completed, just consume it so the concrete actor never sees it
                    break;
                default:
                    return false;
            }

            return true;
        }
    }
}
