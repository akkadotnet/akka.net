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

        public static Props Props(string tag, long fromOffset, long toOffset, TimeSpan? refreshInterval, int maxBufferSize, IActorRef writeJournal, bool isThrottled)
        {
            return refreshInterval.HasValue
                ? Actor.Props.Create(() => new LiveEventsByTagPublisher(tag, fromOffset, toOffset, refreshInterval.Value, maxBufferSize, writeJournal, isThrottled))
                : Actor.Props.Create(() => new CurrentEventsByTagPublisher(tag, fromOffset, toOffset, maxBufferSize, writeJournal, isThrottled));
        }
    }

    internal abstract class AbstractEventsByTagPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;

        protected readonly DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected long CurrentOffset;
        private readonly bool _isThrottled;
        protected AbstractEventsByTagPublisher(string tag, long fromOffset, int maxBufferSize, IActorRef writeJournal, bool isThrottled)
        {
            Tag = tag;
            CurrentOffset = FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            JournalRef = writeJournal;
            _isThrottled = isThrottled;
        }

        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        protected string Tag { get; }
        protected long FromOffset { get; }
        protected abstract long ToOffset { get; }
        protected int MaxBufferSize { get; }

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    ReceiveInitialRequest();
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

        protected void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("request replay for tag [{0}] from [{1}] to [{2}] limit [{3}]", Tag, CurrentOffset, ToOffset, limit);
            // if we're throttling, messages need to go back to throttler, not ourselves
            JournalRef.Tell(new ReplayTaggedMessages(CurrentOffset, ToOffset, limit, Tag, _isThrottled ? JournalRef : Self));
            Context.Become(Replaying(limit));
        }

        protected Receive Replaying(int limit)
        {
            return message =>
            {
                switch (message)
                {
                    case ReplayedTaggedMessage replayed:
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
                        Log.Debug("replay completed for tag [{0}], currOffset [{1}]", Tag, CurrentOffset);
                        ReceiveRecoverySuccess(success.HighestSequenceNr);
                        return true;
                    
                    case ReplayMessagesFailure failure:
                        Log.Debug("replay failed for tag [{0}], due to [{1}]", Tag, failure.Cause.Message);
                        Buffer.DeliverBuffer(TotalDemand);
                        OnErrorThenStop(failure.Cause);
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
            };
        }
    }

    internal sealed class LiveEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveEventsByTagPublisher(string tag, long fromOffset, long toOffset, TimeSpan refreshInterval, int maxBufferSize, IActorRef writeJournal, bool isThrottler)
            : base(tag, fromOffset, maxBufferSize, writeJournal, isThrottler)
        {
            ToOffset = toOffset;
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, EventsByTagPublisher.Continue.Instance, Self);
        }

        protected override long ToOffset { get; }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveInitialRequest()
        {
            Replay();
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
        public CurrentEventsByTagPublisher(string tag, long fromOffset, long toOffset, int maxBufferSize, IActorRef writeJournal, bool isThrottled)
            : base(tag, fromOffset, maxBufferSize, writeJournal, isThrottled)
        {
            _toOffset = toOffset;
        }

        private long _toOffset;
        protected override long ToOffset => _toOffset;
        protected override void ReceiveInitialRequest()
        {
            Replay();
        }

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
