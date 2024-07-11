//-----------------------------------------------------------------------
// <copyright file="EventsByPersistenceIdPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sql
{
    internal static class EventsByPersistenceIdPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new();

            private Continue()
            {
            }
        }

        public static Props Props(string persistenceId, long fromSequenceNr, long toSequenceNr,
            TimeSpan? refreshDuration, int maxBufferSize, IActorRef writeJournal, IActorRef queryPermitter)
        {
            return refreshDuration.HasValue
                ? Actor.Props.Create(() => new LiveEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr,
                    toSequenceNr, maxBufferSize, writeJournal, refreshDuration.Value, queryPermitter))
                : Actor.Props.Create(() => new CurrentEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr,
                    toSequenceNr, maxBufferSize, writeJournal, queryPermitter));
        }
    }

    internal abstract class AbstractEventsByPersistenceIdPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;

        protected DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected readonly IActorRef QueryPermitter;
        protected long CurrentSequenceNr;

        protected AbstractEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef journalRef, IActorRef queryPermitter)
        {
            PersistenceId = persistenceId;
            CurrentSequenceNr = FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            MaxBufferSize = maxBufferSize;
            JournalRef = journalRef;
            QueryPermitter = queryPermitter;
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
                    if (IsTimeForReplay) RequestQueryPermit();
                    return true;
                case Request _:
                    ReceiveIdleRequest();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        protected void RequestQueryPermit()
        {
            Log.Debug("Requesting query permit");
            QueryPermitter.Tell(RequestQueryStart.Instance);
            Become(WaitingForQueryPermit);
        }

        protected bool WaitingForQueryPermit(object message)
        {
            switch (message)
            {
                case QueryStartGranted _:
                    Replay();
                    return true;
                case EventsByPersistenceIdPublisher.Continue _:
                    // ignore
                    return true;
                case Request _:
                    ReceiveIdleRequest(); // can still handle idle requests while waiting for permit
                    return true;
                case Cancel _:
                    Context.Stop(Self);
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
        }

        protected bool Replaying(object message)
        {
            switch (message)
            {
                case ReplayedMessage replayed:
                    var seqNr = replayed.Persistent.SequenceNr;
                    // NOTES: tags is empty because tags are not retrieved from the database query (as of this writing)
                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(seqNr),
                        persistenceId: PersistenceId,
                        sequenceNr: seqNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp,
                        tags: Array.Empty<string>()));
                    CurrentSequenceNr = seqNr + 1;
                    Buffer.DeliverBuffer(TotalDemand);
                    return true;

                case RecoverySuccess success:
                    Log.Debug("replay completed for persistenceId [{0}], currSeqNo [{1}]", PersistenceId,
                        CurrentSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;

                case ReplayMessagesFailure failure:
                    Log.Debug("replay failed for persistenceId [{0}], due to [{1}]", PersistenceId,
                        failure.Cause.Message);
                    Buffer.DeliverBuffer(TotalDemand);
                    OnErrorThenStop(failure.Cause);
                    QueryPermitter.Tell(ReturnQueryStart.Instance); // return token
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
    }

    internal sealed class LiveEventsByPersistenceIdPublisher : AbstractEventsByPersistenceIdPublisher
    {
        private readonly ICancelable _tickCancelable;

        public LiveEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef writeJournal, TimeSpan refreshInterval, IActorRef queryPermitter)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, queryPermitter)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval,
                refreshInterval, Self, EventsByPersistenceIdPublisher.Continue.Instance, Self);
        }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveInitialRequest()
        {
            RequestQueryPermit();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            QueryPermitter.Tell(ReturnQueryStart.Instance); // return token
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByPersistenceIdPublisher : AbstractEventsByPersistenceIdPublisher
    {
        public CurrentEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, IActorRef writeJournal, IActorRef queryPermitter)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, queryPermitter)
        {
        }

        protected override void ReceiveInitialRequest()
        {
            RequestQueryPermit();
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
            QueryPermitter.Tell(ReturnQueryStart.Instance); // return token
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
