//-----------------------------------------------------------------------
// <copyright file="EventsByPersistenceIdPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;
using static Akka.Persistence.Query.Sql.SqlQueryConstants;

namespace Akka.Persistence.Query.Sql
{
    internal static class EventsByPersistenceIdPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue()
            {
            }
        }

        public static Props Props(string persistenceId, long fromSequenceNr, long toSequenceNr,
            TimeSpan? refreshDuration, int maxBufferSize, IActorRef writeJournal)
        {
            return refreshDuration.HasValue
                ? Actor.Props.Create(() => new LiveEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr,
                    toSequenceNr, maxBufferSize, writeJournal, refreshDuration.Value))
                : Actor.Props.Create(() => new CurrentEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr,
                    toSequenceNr, maxBufferSize, writeJournal));
        }
    }

    internal abstract class AbstractEventsByPersistenceIdPublisher : ActorPublisher<EventEnvelope>, IWithTimers
    {
        /// <summary>
        /// Used to trigger a client-side timeout
        /// </summary>
        private class RecoveryOverDue
        {
            public static readonly RecoveryOverDue Instance = new RecoveryOverDue();

            private RecoveryOverDue()
            {
            }
        }

        private const string SingleRecoveryTimeoutKey = "recover-1";

        private ILoggingAdapter _log;

        protected DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected long CurrentSequenceNr;

        protected AbstractEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef journalRef)
        {
            PersistenceId = persistenceId;
            CurrentSequenceNr = FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            MaxBufferSize = maxBufferSize;
            JournalRef = journalRef;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
        }

        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        protected string PersistenceId { get; }
        protected long FromSequenceNr { get; }
        protected long ToSequenceNr { get; set; }
        protected int MaxBufferSize { get; }

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) &&
                                          (CurrentSequenceNr <= ToSequenceNr);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);

        protected override bool Receive(object message)
        {
            return Init(message);
        }

        protected bool Init(object message)
        {
            switch (message)
            {
                case EventsByPersistenceIdPublisher.Continue _:
                    // no-op
                    return true;
                case Request _:
                    ReceiveInitialRequest();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        protected bool Idle(object message)
        {
            switch (message)
            {
                case EventsByPersistenceIdPublisher.Continue _:
                    if (IsTimeForReplay) Replay();
                    return true;
                case Request _:
                    ReceiveIdleRequest();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                case RecoveryOverDue _:
                case Status.Failure _: // ignore
                    return true;
                default:
                    return false;
            }
        }

        protected void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("request replay for persistenceId [{0}] from [{1}] to [{2}] limit [{3}]", PersistenceId,
                CurrentSequenceNr, ToSequenceNr, limit);
            JournalRef.Tell(new ReplayMessages(CurrentSequenceNr, ToSequenceNr, limit, PersistenceId, Self));
            Context.Become(Replaying);
            Timers.StartSingleTimer(SingleRecoveryTimeoutKey, RecoveryOverDue.Instance, DefaultQueryTimeout);
        }

        protected bool Replaying(object message)
        {
            switch (message)
            {
                // received an older message - might have been from a request we timed out earlier
                case ReplayedMessage replayed when replayed.Persistent.SequenceNr < CurrentSequenceNr:
                    _log.Debug("Received message with SeqNo [{0}] that is lower than current SeqNo [{1}]. Skipping.",
                        replayed.Persistent.SequenceNr, CurrentSequenceNr);
                    return true;
                case ReplayedMessage replayed:
                    var seqNr = replayed.Persistent.SequenceNr;
                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(seqNr),
                        persistenceId: PersistenceId,
                        sequenceNr: seqNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp));
                    CurrentSequenceNr = seqNr + 1;
                    Buffer.DeliverBuffer(TotalDemand);

                    // cancel the timer as soon as we get the first response
                    Timers.Cancel(SingleRecoveryTimeoutKey);
                    return true;

                case RecoverySuccess success:
                    Log.Debug("replay completed for persistenceId [{0}], currSeqNo [{1}]", PersistenceId,
                        CurrentSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;

                case Status.Failure _: // timed out by throttler
                case RecoveryOverDue _: // timed out by query
                    Log.Debug("replay timed out for persistenceId [{0}] after [{1}] - retrying", PersistenceId,
                        DefaultQueryTimeout);
                    Become(Idle);
                    Self.Tell(EventsByPersistenceIdPublisher.Continue.Instance);
                    return true;

                case ReplayMessagesFailure failure: // treat this as a hard failure and crash
                    Log.Error(failure.Cause,
                        "replay failed for persistenceId [{0}], due to [{1}] - critical failure. Stopping query.",
                        PersistenceId,
                        failure.Cause.Message);
                    Buffer.DeliverBuffer(TotalDemand);
                    OnErrorThenStop(failure.Cause);
                    return true;

                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
                    return true;

                case EventsByPersistenceIdPublisher.Continue _:
                    // skip during replay
                    return true;

                case Cancel _:
                    Context.Stop(Self);
                    return true;

                default:
                    return false;
            }
        }

        public ITimerScheduler Timers { get; set; }
    }

    internal sealed class LiveEventsByPersistenceIdPublisher : AbstractEventsByPersistenceIdPublisher
    {
        private const string ContinueTimerKey = "continue";

        public LiveEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef writeJournal, TimeSpan refreshInterval)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal)
        {
            Timers.StartPeriodicTimer(ContinueTimerKey, EventsByPersistenceIdPublisher.Continue.Instance,
                refreshInterval);
        }

        protected override void ReceiveInitialRequest()
        {
            Replay();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByPersistenceIdPublisher : AbstractEventsByPersistenceIdPublisher
    {
        public CurrentEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef writeJournal)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal)
        {
        }

        protected override void ReceiveInitialRequest()
        {
            Replay();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByPersistenceIdPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (highestSequenceNr < ToSequenceNr)
                ToSequenceNr = highestSequenceNr;
            if (Buffer.IsEmpty && (CurrentSequenceNr > ToSequenceNr || CurrentSequenceNr == FromSequenceNr))
                OnCompleteThenStop();
            else
                Self.Tell(EventsByPersistenceIdPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}