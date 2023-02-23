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

        public static Props Props(string persistenceId, long fromSequenceNr, long toSequenceNr, TimeSpan? refreshDuration, int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
        {
            return refreshDuration.HasValue
                ? Actor.Props.Create(() => new LiveEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, refreshDuration.Value, settings))
                : Actor.Props.Create(() => new CurrentEventsByPersistenceIdPublisher(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, settings));
        }
    }

    internal abstract class AbstractEventsByPersistenceIdPublisher : ActorPublisher<EventEnvelope>, IWithTimers
    {
        private readonly string _retryKey = nameof(_retryKey);
        private readonly string _operationTimeoutKey = nameof(_operationTimeoutKey);

        private int _retries;
        private readonly int _maxRetries;
        private readonly TimeSpan _maxBackoff;
        private readonly float _backoffMultiplier;
        private readonly TimeSpan _backoffTime;
        private readonly TimeSpan _operationTimeout;
        
        protected DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected long CurrentSequenceNr;

        protected AbstractEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr, int maxBufferSize, IActorRef journalRef, QuerySettings settings)
        {
            PersistenceId = persistenceId;
            CurrentSequenceNr = FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            MaxBufferSize = maxBufferSize;
            JournalRef = journalRef;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            
            _operationTimeout = settings.OperationTimeout;
            _maxRetries = settings.MaxRetries;
            _maxBackoff = settings.MaxBackoff;
            _backoffMultiplier = settings.BackoffMultiplier;
            _backoffTime = settings.BackoffTime;

            Log = Context.GetLogger();
        }

        public ITimerScheduler Timers { get; set; }
        protected ILoggingAdapter Log { get; }
        protected string PersistenceId { get; }
        protected long FromSequenceNr { get; }
        protected long ToSequenceNr { get; set; }
        protected int MaxBufferSize { get; }

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentSequenceNr <= ToSequenceNr);

        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case EventsByPersistenceIdPublisher.Continue _:
                    // no-op
                    return true;
                case Request _:
                    Replay();
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
                default:
                    return false;
            }
        }

        private bool Retrying(object message)
        {
            switch (message)
            {
                case Retry _:
                    Become(Replaying);
                    Replay();
                    return true;
                case EventsByTagPublisher.Continue _:
                    // no-op
                    return true;
                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
                    if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                        OnCompleteThenStop();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        private void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("request replay for persistenceId [{0}] from [{1}] to [{2}] limit [{3}]", 
                PersistenceId, CurrentSequenceNr, ToSequenceNr, limit);
            JournalRef.Tell(new ReplayMessages(CurrentSequenceNr, ToSequenceNr, limit, PersistenceId, Self, _operationTimeout));
            Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
            Context.Become(Replaying);
        }

        private bool Replaying(object message)
        {
            switch (message)
            {
                case ReplayedMessage replayed:
                    var seqNr = replayed.Persistent.SequenceNr;
                    // discard stale messages
                    if (seqNr < CurrentSequenceNr)
                        return true;
                    
                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(seqNr),
                        persistenceId: PersistenceId,
                        sequenceNr: seqNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp));
                    CurrentSequenceNr = seqNr + 1;
                    Buffer.DeliverBuffer(TotalDemand);
                    if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                        OnCompleteThenStop();
                    return true;

                case RecoverySuccess success:
                    Timers.CancelAll();
                    _retries = 0;
                    Log.Debug("replay completed for persistenceId [{0}], currSeqNo [{1}]", PersistenceId,
                        CurrentSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;

                case ReplayMessagesFailure failure:
                    Timers.CancelAll();
                    RetryQuery(failure.Cause);
                    return true;

                case Status.Failure msg:
                    Timers.CancelAll();
                    RetryQuery(msg.Cause);
                    return true;

                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
                    if (Buffer.IsEmpty && CurrentSequenceNr > ToSequenceNr)
                        OnCompleteThenStop();
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

        private void RetryQuery(Exception ex)
        {
            _retries++;
            if (_retries > _maxRetries)
            {
                Buffer.DeliverBuffer(TotalDemand);
                Log.Debug(ex, "Events by persistence ID query failed due to [{0}]. Maximum retry reached", ex.Message);
                OnErrorThenStop(ex);
                return;
            }
            
            Become(Retrying);
            var retryTime = new TimeSpan((long)(_backoffTime.Ticks * (_backoffMultiplier * _retries)));
            if (retryTime > _maxBackoff)
                retryTime = _maxBackoff;
            Log.Debug(ex, "Events by persistence ID query failed due to [{0}]. Retrying in {1} ms ({2}/{3})", 
                ex.Message, retryTime.TotalMilliseconds, _retries, _maxRetries);
            Timers.StartSingleTimer(_retryKey, Retry.Instance, retryTime);
        }
    }

    internal sealed class LiveEventsByPersistenceIdPublisher : AbstractEventsByPersistenceIdPublisher
    {
        private readonly ICancelable _tickCancelable;

        public LiveEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr, int maxBufferSize, IActorRef writeJournal, TimeSpan refreshInterval, QuerySettings settings)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, settings)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, EventsByPersistenceIdPublisher.Continue.Instance, Self);
        }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
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
        public CurrentEventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr, int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
            : base(persistenceId, fromSequenceNr, toSequenceNr, maxBufferSize, writeJournal, settings)
        {
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
            if(Buffer.IsEmpty && (CurrentSequenceNr > ToSequenceNr || CurrentSequenceNr == FromSequenceNr))
                OnCompleteThenStop();
            else
                Self.Tell(EventsByPersistenceIdPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}
