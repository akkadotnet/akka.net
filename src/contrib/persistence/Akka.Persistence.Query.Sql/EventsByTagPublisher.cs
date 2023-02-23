//-----------------------------------------------------------------------
// <copyright file="EventsByTagPublisher.cs" company="Akka.NET Project">
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
    internal static class EventsByTagPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue()
            {
            }
        }

        public static Props Props(string tag, long fromOffset, long toOffset, TimeSpan? refreshInterval,
            int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
        {
            return refreshInterval.HasValue
                ? Actor.Props.Create(() => new LiveEventsByTagPublisher(tag, fromOffset, toOffset,
                    refreshInterval.Value, maxBufferSize, writeJournal, settings))
                : Actor.Props.Create(() =>
                    new CurrentEventsByTagPublisher(tag, fromOffset, toOffset, maxBufferSize, writeJournal, settings));
        }
    }

    internal abstract class AbstractEventsByTagPublisher : ActorPublisher<EventEnvelope>, IWithTimers
    {
        private readonly string _retryKey = nameof(_retryKey);
        private readonly string _operationTimeoutKey = nameof(_operationTimeoutKey);
        
        private int _retries;
        private readonly int _maxRetries;
        private readonly TimeSpan _maxBackoff;
        private readonly float _backoffMultiplier;
        private readonly TimeSpan _backoffTime;
        private readonly TimeSpan _operationTimeout;
        private ILoggingAdapter _log;

        protected readonly DeliveryBuffer<EventEnvelope> Buffer;
        private readonly IActorRef _journalRef;
        protected long CurrentOffset;

        protected AbstractEventsByTagPublisher(
            string tag,
            long fromOffset,
            int maxBufferSize,
            IActorRef writeJournal,
            QuerySettings settings)
        {
            Tag = tag;
            CurrentOffset = FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            _journalRef = writeJournal;
            
            _operationTimeout = settings.OperationTimeout;
            _maxRetries = settings.MaxRetries;
            _maxBackoff = settings.MaxBackoff;
            _backoffMultiplier = settings.BackoffMultiplier;
            _backoffTime = settings.BackoffTime;
        }

        public ITimerScheduler Timers { get; set; }

        private ILoggingAdapter Log => _log ??= Context.GetLogger();
        private string Tag { get; }
        private long FromOffset { get; }
        protected abstract long ToOffset { get; }
        private int MaxBufferSize { get; }

        private bool IsTimeForReplay =>
            Buffer.Length <= MaxBufferSize / 2 && CurrentOffset <= ToOffset;

        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);

        protected override void PostStop()
        {
            Timers.CancelAll();
            base.PostStop();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Replay();
                    return true;
                case EventsByTagPublisher.Continue _:
                    // no-op
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
                case EventsByTagPublisher.Continue _:
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
            Log.Debug("Request replay for tag [{0}] from [{1}] to [{2}] limit [{3}]", 
                Tag, CurrentOffset, ToOffset, limit);
            _journalRef.Tell(new ReplayTaggedMessages(CurrentOffset, ToOffset, limit, Tag, Self, _operationTimeout));
            Timers.StartSingleTimer(_operationTimeoutKey, OperationTimedOut.Instance, _operationTimeout);
            Context.Become(Replaying);
        }

        private bool Replaying(object message)
        {
            switch (message)
            {
                case ReplayedTaggedMessage replayed:
                    // discard stale messages
                    if (replayed.Offset <= CurrentOffset)
                        return true;
                    
                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(replayed.Offset),
                        persistenceId: replayed.Persistent.PersistenceId,
                        sequenceNr: replayed.Persistent.SequenceNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp));

                    CurrentOffset = replayed.Offset;
                    Buffer.DeliverBuffer(TotalDemand);
                    return true;

                case RecoverySuccess success:
                    Timers.CancelAll();
                    _retries = 0;
                    Log.Debug("replay completed for tag [{0}], currOffset [{1}]", Tag, CurrentOffset);
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

                case OperationTimedOut _:
                    Exception ex;
                    try
                    {
                        throw new TimeoutException("Query timed out");
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                    RetryQuery(ex);
                    return true;

                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
                    return true;

                case EventsByTagPublisher.Continue _:
                    // no-op
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
                Log.Debug(ex, "Replay failed for tag [{0}] due to [{1}]. Maximum retry reached", Tag, ex.Message);
                OnErrorThenStop(ex);
                return;
            }
            
            Become(Retrying);
            var retryTime = new TimeSpan((long)(_backoffTime.Ticks * (_backoffMultiplier * _retries)));
            if (retryTime > _maxBackoff)
                retryTime = _maxBackoff;
            Log.Debug(ex, "Replay failed for tag [{0}] due to [{1}]. Retrying in {2} ms ({3}/{4})", 
                Tag, ex.Message, retryTime.TotalMilliseconds, _retries, _maxRetries);
            Timers.StartSingleTimer(_retryKey, Retry.Instance, retryTime);
        }
    }

    internal sealed class LiveEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        private readonly ICancelable _tickCancelable;

        public LiveEventsByTagPublisher(string tag, long fromOffset, long toOffset, TimeSpan refreshInterval,
            int maxBufferSize, IActorRef writeJournal, QuerySettings settings)
            : base(tag, fromOffset, maxBufferSize, writeJournal, settings)
        {
            ToOffset = toOffset;
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval,
                refreshInterval, Self, EventsByTagPublisher.Continue.Instance, Self);
        }

        protected override long ToOffset { get; }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        public CurrentEventsByTagPublisher(string tag, long fromOffset, long toOffset, int maxBufferSize,
            IActorRef writeJournal, QuerySettings settings)
            : base(tag, fromOffset, maxBufferSize, writeJournal, settings)
        {
            _toOffset = toOffset;
        }

        private long _toOffset;
        protected override long ToOffset => _toOffset;

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (highestSequenceNr < ToOffset)
                _toOffset = highestSequenceNr;

            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}