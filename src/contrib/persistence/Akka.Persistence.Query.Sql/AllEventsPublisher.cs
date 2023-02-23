//-----------------------------------------------------------------------
// <copyright file="AllEventsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sql
{
    internal static class AllEventsPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue() { }
        }

        public static Props Props(long fromOffset, TimeSpan? refreshInterval, int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
        {
            return refreshInterval.HasValue ?
                Actor.Props.Create(() => new LiveAllEventsPublisher(fromOffset, refreshInterval.Value, maxBufferSize, writeJournal, settings)) :
                Actor.Props.Create(() => new CurrentAllEventsPublisher(fromOffset, maxBufferSize, writeJournal, settings));
        }
    }

    internal abstract class AbstractAllEventsPublisher : ActorPublisher<EventEnvelope>, IWithTimers
    {
        private readonly string _retryKey = nameof(_retryKey);
        private readonly string _operationTimeoutKey = nameof(_operationTimeoutKey);

        private int _retries;
        private readonly int _maxRetries;
        private readonly TimeSpan _maxBackoff;
        private readonly float _backoffMultiplier;
        private readonly TimeSpan _backoffTime;
        private readonly TimeSpan _operationTimeout;
        
        protected long CurrentOffset;

        protected AbstractAllEventsPublisher(long fromOffset, int maxBufferSize, IActorRef journalRef, QuerySettings settings)
        {
            CurrentOffset = FromOffset = fromOffset;
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
        private ILoggingAdapter Log { get; }
        private IActorRef JournalRef { get; }
        protected DeliveryBuffer<EventEnvelope> Buffer { get; }
        private long FromOffset { get; }
        protected abstract long ToOffset { get; }
        private int MaxBufferSize { get; }
        private bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestOrderingNr);

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Replay();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                case AllEventsPublisher.Continue _:
                    return true;
                default:
                    return false;
            }
        }

        protected bool Idle(object message)
        {
            switch (message)
            {
                case AllEventsPublisher.Continue _:
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
                    if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
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
            if (CurrentOffset >= ToOffset)
                return;
            
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("replay all events request from [{0}] to [{1}], limit [{2}]", CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new ReplayAllEvents(CurrentOffset, ToOffset, limit, Self, _operationTimeout));
            Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
            Context.Become(Replaying);
        }

        private bool Replaying( object message )
        {
            switch (message)
            {
                case ReplayedEvent replayed:
                    // ReplayEvent might overshoot the current ToOffset target
                    if (replayed.Offset > ToOffset)
                        return true;

                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(replayed.Offset),
                        persistenceId: replayed.Persistent.PersistenceId,
                        sequenceNr: replayed.Persistent.SequenceNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp));

                    CurrentOffset = replayed.Offset;
                    Buffer.DeliverBuffer(TotalDemand);
                    if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                        OnCompleteThenStop();
                    return true;
                
                case EventReplaySuccess success:
                    Timers.CancelAll();
                    Log.Debug("event replay completed, currOffset [{0}], highestSequenceNr [{1}]", CurrentOffset, success.HighestSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;
                
                case EventReplayFailure failure:
                    Timers.CancelAll();
                    RetryQuery(failure.Cause);
                    return true;
                
                case Status.Failure msg:
                    Timers.CancelAll();
                    RetryQuery(msg.Cause);
                    return true;

                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
                    if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                        OnCompleteThenStop();
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case AllEventsPublisher.Continue _:
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
                Log.Debug(ex, "All Events query failed due to [{0}]. Maximum retry reached", ex.Message);
                OnErrorThenStop(ex);
                return;
            }
            
            Become(Retrying);
            var retryTime = new TimeSpan((long)(_backoffTime.Ticks * (_backoffMultiplier * _retries)));
            if (retryTime > _maxBackoff)
                retryTime = _maxBackoff;
            Log.Debug(ex, "All events query failed due to [{0}]. Retrying in {1} ms ({2}/{3})", 
                ex.Message, retryTime.TotalMilliseconds, _retries, _maxRetries);
            Timers.StartSingleTimer(_retryKey, Retry.Instance, retryTime);
        }
    }

    internal sealed class LiveAllEventsPublisher : AbstractAllEventsPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveAllEventsPublisher(long fromOffset, TimeSpan refreshInterval, int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
            : base(fromOffset, maxBufferSize, writeJournal, settings)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, AllEventsPublisher.Continue.Instance, Self);
        }

        protected override long ToOffset => long.MaxValue;

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestOrderingNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentAllEventsPublisher : AbstractAllEventsPublisher
    {
        public CurrentAllEventsPublisher(long fromOffset, int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
            : base(fromOffset, maxBufferSize, writeJournal, settings)
        { }

        private long _toOffset = long.MaxValue;
        protected override long ToOffset => _toOffset;

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(AllEventsPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestOrderingNr)
        {
            Buffer.DeliverBuffer(TotalDemand);

            if (highestOrderingNr < ToOffset)
                _toOffset = highestOrderingNr;

            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(AllEventsPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}
