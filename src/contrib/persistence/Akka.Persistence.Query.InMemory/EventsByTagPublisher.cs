//-----------------------------------------------------------------------
// <copyright file="EventsByTagPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.InMemory
{
    internal static class EventsByTagPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new();

            private Continue()
            {
            }
        }

        public static Props Props(string tag, int fromOffset, int toOffset, TimeSpan? refreshInterval, int maxBufferSize, string writeJournalPluginId)
        {
            return refreshInterval.HasValue
                ? Actor.Props.Create(() => new LiveEventsByTagPublisher(tag, fromOffset, toOffset, refreshInterval.Value, maxBufferSize, writeJournalPluginId))
                : Actor.Props.Create(() => new CurrentEventsByTagPublisher(tag, fromOffset, toOffset, maxBufferSize, writeJournalPluginId));
        }
    }

    internal abstract class AbstractEventsByTagPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;

        protected readonly DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected int CurrentOffset;
        
        protected AbstractEventsByTagPublisher(string tag, int fromOffset, int maxBufferSize, string writeJournalPluginId)
        {
            Tag = tag;
            CurrentOffset = FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            WriteJournalPluginId = writeJournalPluginId;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        protected string Tag { get; }
        protected int FromOffset { get; }
        protected abstract int ToOffset { get; }
        protected int MaxBufferSize { get; }
        protected string WriteJournalPluginId { get; }

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(int highestSequenceNr);

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
            JournalRef.Tell(new MemoryJournal.ReplayTaggedMessages(CurrentOffset, ToOffset, limit, Tag, Self));
            Context.Become(Replaying(limit));
        }

        protected Receive Replaying(int limit)
        {
            return message =>
            {
                switch (message)
                {
                    case MemoryJournal.ReplayedTaggedMessage replayed:
                        Buffer.Add(new EventEnvelope(
                            offset: new Sequence(replayed.Offset),
                            persistenceId: replayed.Persistent.PersistenceId,
                            sequenceNr: replayed.Persistent.SequenceNr,
                            @event: replayed.Persistent.Payload,
                            timestamp: replayed.Persistent.Timestamp,
                            tags: new [] { replayed.Tag }));

                        CurrentOffset = replayed.Offset + 1;
                        Buffer.DeliverBuffer(TotalDemand);
                        return true;
                    
                    case MemoryJournal.ReplayTaggedMessagesSuccess success:
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
        public LiveEventsByTagPublisher(string tag, int fromOffset, int toOffset, TimeSpan refreshInterval, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
        {
            ToOffset = toOffset;
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, EventsByTagPublisher.Continue.Instance, Self);
        }

        protected override int ToOffset { get; }

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

        protected override void ReceiveRecoverySuccess(int highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        public CurrentEventsByTagPublisher(string tag, int fromOffset, int toOffset, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
        {
            _toOffset = toOffset;
        }

        private int _toOffset;
        protected override int ToOffset => _toOffset;
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

        protected override void ReceiveRecoverySuccess(int highestSequenceNr)
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
