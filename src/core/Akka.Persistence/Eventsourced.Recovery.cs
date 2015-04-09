//-----------------------------------------------------------------------
// <copyright file="Eventsourced.Recovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Persistence
{
    internal delegate void StateReceive(Receive receive, object message);

    internal class EventsourcedState
    {
        public EventsourcedState(string name, bool isRecoveryRunning, StateReceive stateReceive)
        {
            Name = name;
            IsRecoveryRunning = isRecoveryRunning;
            StateReceive = stateReceive;
        }

        public string Name { get; private set; }
        public bool IsRecoveryRunning { get; private set; }
        public StateReceive StateReceive { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public abstract partial class Eventsourced
    {
        /// <summary>
        /// Initial state, waits for <see cref="Recover"/> request, and then submits a <see cref="LoadSnapshot"/> request to the snapshot
        /// store and changes to <see cref="RecoveryStarted"/> state. All incoming messages except <see cref="Recover"/> are stashed.
        /// </summary>
        /// <returns></returns>
        private EventsourcedState RecoveryPending()
        {
            return new EventsourcedState("recovery pending", true, (receive, message) =>
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
        /// Processes a loaded snapshot, if any. A loaded snapshot is offered with a <see cref="SnapshotOffer"/> 
        /// message to the actor's <see cref="ReceiveRecover"/>. Then initiates a message replay, either starting 
        /// from the loaded snapshot or from scratch, and switches to <see cref="ReplayStarted"/> state. 
        /// All incoming messages are stashed.
        /// </summary>
        /// <param name="maxReplays">Maximum number of messages to replay</param>
        private EventsourcedState RecoveryStarted(long maxReplays)
        {
            Receive recoveryBehavior = message =>
            {
                Receive receiveRecover = ReceiveRecover;
                if (message is IPersistentRepresentation && IsRecovering)
                    return receiveRecover((message as IPersistentRepresentation).Payload);
                else if (message is SnapshotOffer)
                    return receiveRecover((SnapshotOffer)message);
                else if (message is RecoveryFailure)
                    return receiveRecover(message);
                else if (message is RecoveryCompleted)
                    return receiveRecover(RecoveryCompleted.Instance);
                else return false;
            };

            return new EventsourcedState("recovery started - replay max: " + maxReplays, true, (receive, message) =>
            {
                if (message is Recover) return;
                if (message is LoadSnapshotResult)
                {
                    var res = (LoadSnapshotResult)message;
                    if (res.Snapshot != null)
                    {
                        var snapshot = res.Snapshot;
                        LastSequenceNr = snapshot.Metadata.SequenceNr;
                        base.AroundReceive(recoveryBehavior, new SnapshotOffer(snapshot.Metadata, snapshot.Snapshot));
                    }

                    ChangeState(ReplayStarted(recoveryBehavior));
                    Journal.Tell(new ReplayMessages(LastSequenceNr + 1L, res.ToSequenceNr, maxReplays, PersistenceId, Self));
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Processes replayed messages, if any. The actor's <see cref="ReceiveRecover"/> is invoked with the replayed events.
        /// 
        /// If replay succeeds it switches to <see cref="Initializing"/> state and requests the highest stored sequence
        /// number from the journal. Otherwise RecoveryFailure is emitted.
        /// If replay succeeds the `onReplaySuccess` callback method is called, otherwise `onReplayFailure`.
        /// 
        /// If processing of a replayed event fails, the exception is caught and
        /// stored for being thrown later and state is changed to <see cref="RecoveryFailed"/>.
        /// 
        /// All incoming messages are stashed.
        /// </summary>
        private EventsourcedState ReplayStarted(Receive recoveryBehavior)
        {
            return new EventsourcedState("replay started", true, (receive, message) =>
            {
                if (message is Recover) return;
                if (message is ReplayedMessage)
                {
                    var m = (ReplayedMessage)message;
                    try
                    {
                        UpdateLastSequenceNr(m.Persistent);
                        base.AroundReceive(recoveryBehavior, m.Persistent);
                    }
                    catch (Exception exc)
                    {
                        var currentMessage = Context.AsInstanceOf<ActorCell>().CurrentMessage;
                        ChangeState(ReplayFailed(exc, currentMessage));
                    }
                }
                else if (message is ReplayMessagesSuccess)
                {
                    OnReplaySuccess();
                    ChangeState(Initializing(recoveryBehavior));
                    Journal.Tell(new ReadHighestSequenceNr(LastSequenceNr, PersistenceId, Self));
                }
                else if (message is ReplayMessagesFailure)
                {
                    var failure = (ReplayMessagesFailure)message;
                    OnReplayFailure(failure.Cause);
                    // FIXME what happens if RecoveryFailure is handled, i.e. actor is not stopped?
                    base.AroundReceive(recoveryBehavior, new RecoveryFailure(failure.Cause));
                }
                else _internalStash.Stash();
            });
        }

        /// <summary>
        /// Processes all remaining replayed messages and changes to <see cref="PrepareRestart"/>.
        /// Message that caused and exception during replay, is re-added to the mailbox and re-received
        /// in <see cref="PrepareRestart"/> state.
        /// </summary>
        private EventsourcedState ReplayFailed(Exception cause, object failureMessage)
        {
            return new EventsourcedState("replay failed", true, (receive, message) =>
            {
                if (message is ReplayMessagesFailure)
                {
                    ReplayCompleted(cause, failureMessage);
                    // journal couldn't tell the maximum stored sequence number, hence the next
                    // replay must be a full replay (up to the highest stored sequence number)
                    // Recover(lastSequenceNr) is sent by PreRestart
                    LastSequenceNr = long.MaxValue;
                }
                else if (message is ReplayMessagesSuccess) ReplayCompleted(cause, failureMessage);
                else if (message is ReplayedMessage) UpdateLastSequenceNr(((ReplayedMessage)message).Persistent);
                else if (message is Recover) ;  // ignore
                else _internalStash.Stash();
            });
        }

        private void ReplayCompleted(Exception cause, object failureMessage)
        {
            ChangeState(PrepareRestart(cause));

            //TODO: this implementation requires mailbox.EnqueueFirst to be available, but that actually gives a large 
            // amount of composition constrains. If any of the casts below will go wrong, user-defined messages won't be handled by actors.
            Context.EnqueueMessageFirst(failureMessage);
        }

        /// <summary>
        /// Re-receives replayed message that caused an exception and re-throws the <paramref name="cause"/>.
        /// </summary>
        private EventsourcedState PrepareRestart(Exception cause)
        {
            return new EventsourcedState("prepare restart", true, (receive, message) =>
            {
                if (message is ReplayedMessage) throw cause;
            });
        }

        /// <summary>
        /// Processes messages with the highest stored sequence number in the journal and then switches to
        /// <see cref="ProcessingCommands"/> state. All other messages are stashed.
        /// </summary>
        private EventsourcedState Initializing(Receive recoveryBehavior)
        {
            return new EventsourcedState("initializing", true, (receive, message) =>
            {
                if (message is ReadHighestSequenceNrSuccess)
                {
                    var m = (ReadHighestSequenceNrSuccess)message;
                    ChangeState(ProcessingCommands());
                    _sequenceNr = m.HighestSequenceNr;
                    _internalStash.UnstashAll();

                    base.AroundReceive(recoveryBehavior, RecoveryCompleted.Instance);
                }
                else if (message is ReadHighestSequenceNrFailure)
                {
                    base.AroundReceive(recoveryBehavior, RecoveryCompleted.Instance);
                }
                else _internalStash.Stash();
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
                var handled = CommonProcessingStateBehavior(message, () => _pendingInvocations.Pop());
                if (!handled)
                {
                    base.AroundReceive(receive, message);
                    if (_eventBatch.Count != 0) FlushBatch();

                    if (_pendingStashingPersistInvocations > 0) ChangeState(PersistingEvents());
                    else _internalStash.Unstash();
                }
            });
        }

        private void FlushBatch()
        {
            // When using only `PersistAsync` and `Defer` max throughput is increased by using
            // batching, but when using `Persist` we want to use one atomic WriteMessages
            // for the emitted events.
            // Flush previously collected events, if any, separately from the `Persist` batch
            if (_pendingStashingPersistInvocations > 0 && _journalBatch.Count != 0)
                FlushJournalBatch();

            foreach (var p in _eventBatch.Reverse())
            {
                var persistent = p as Persistent;
                if (persistent != null)
                {
                    _journalBatch.Add(persistent.Update(
                        persistenceId: PersistenceId,
                        sequenceNr: NextSequenceNr(),
                        isDeleted: persistent.IsDeleted,
                        sender: Sender));
                }
                else
                    _journalBatch.Add(p);

                if (!_isWriteInProgress || _journalBatch.Count >= _maxMessageBatchSize)
                    FlushJournalBatch();
            }

            _eventBatch = new LinkedList<IPersistentEnvelope>();
        }

        /// <summary>
        /// Remains until pending events are persisted and then changes state to <see cref="ProcessingCommands"/>.
        /// Only events to be persisted are processed. All other messages are stashed internally.
        /// </summary>
        private EventsourcedState PersistingEvents()
        {
            return new EventsourcedState("persisting events", false, (receive, message) =>
            {
                var handled = CommonProcessingStateBehavior(message, () =>
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
                        _internalStash.Unstash();
                    }
                });

                if (!handled) _internalStash.Stash();
            });
        }

        private bool CommonProcessingStateBehavior(object message, Action onWriteMessageComplete)
        {
            // _instanceId mismatch can happen for persistAsync and defer in case of actor restart
            // while message is in flight, in that case we ignore the call to the handler
            if (message is WriteMessageSuccess)
            {
                var m = (WriteMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    UpdateLastSequenceNr(m.Persistent);
                    TryFirstInvocationHandler(m.Persistent.Payload, onWriteMessageComplete);
                }
            }
            else if (message is WriteMessageFailure)
            {
                var m = (WriteMessageFailure)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    var p = m.Persistent;
                    base.AroundReceive(Receive, new PersistenceFailure(p.Payload, p.SequenceNr, m.Cause));
                    onWriteMessageComplete();
                }
            }
            else if (message is LoopMessageSuccess)
            {
                var m = (LoopMessageSuccess)message;
                if (m.ActorInstanceId == _instanceId)
                {
                    TryFirstInvocationHandler(m.Message, onWriteMessageComplete);
                }
            }
            else if (message is WriteMessagesSuccessful || message is WriteMessagesFailed)
            {
                if (_journalBatch.Count == 0) _isWriteInProgress = false;
                else FlushJournalBatch();
            }
            else return false;
            return true;
        }

        private void TryFirstInvocationHandler(object payload, Action onWriteMessageComplete)
        {
            try
            {
                _pendingInvocations.First.Value.Handler(payload);
            }
            finally
            {
                onWriteMessageComplete();
            }
        }
    }

    internal static class LinkedListExtensions
    {
        /// <summary>
        /// Removes first element from the list and returns it or returns default value if list was empty.
        /// </summary>
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

