//-----------------------------------------------------------------------
// <copyright file="AllEventsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;
using System;

namespace Akka.Persistence.Query.Sql
{
    internal static class AllEventsPublisher
    {
        public sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue()
            {
            }
        }

        public static Props Props(long fromOffset, long toOffset, TimeSpan? refreshDuration, int maxBufferSize, string writeJournalPluginId)
        {
            return refreshDuration.HasValue
                ? Actor.Props.Create(() => new LiveAllEventsPublisher(fromOffset, toOffset, maxBufferSize, writeJournalPluginId, refreshDuration.Value))
                : Actor.Props.Create(() => new CurrentAllEventsPublisher(fromOffset, toOffset, maxBufferSize, writeJournalPluginId));
        }
    }

    internal abstract class AbstractAllEventsPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;

        protected DeliveryBuffer<EventEnvelope> Buffer;
        protected readonly IActorRef JournalRef;
        protected long CurrentOffset;

        protected AbstractAllEventsPublisher(long fromOffset, long toOffset, int maxBufferSize, string writeJournalPluginId)
        {
            FromOffset = CurrentOffset = fromOffset;
            ToOffset = toOffset;
            MaxBufferSize = maxBufferSize;
            WriteJournalPluginId = writeJournalPluginId;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);

            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected long FromOffset { get; }
        protected virtual long ToOffset { get; set; }
        protected int MaxBufferSize { get; }
        protected string WriteJournalPluginId { get; }

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);

        protected override bool Receive(object message)
        {
            return Init(message);
        }

        protected bool Init(object message)
        {
            return message.Match()
                .With<AllEventsPublisher.Continue>(() => { })
                .With<Request>(_ => ReceiveInitialRequest())
                .With<Cancel>(_ => Context.Stop(Self))
                .WasHandled;
        }

        protected bool Idle(object message)
        {
            return message.Match()
                .With<AllEventsPublisher.Continue>(() =>
                {
                    if (IsTimeForReplay) Replay();
                })
                .With<EventAppended>(() =>
                {
                    if (IsTimeForReplay) Replay();
                })
                .With<Request>(_ => ReceiveIdleRequest())
                .With<Cancel>(_ => Context.Stop(Self))
                .WasHandled;
        }

        protected void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("request replay for offset range from [{0}] to [{1}] limit [{2}]", CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new ReplayAllMessages(CurrentOffset, ToOffset, limit, Self));
            Context.Become(Replaying(limit));
        }

        protected Receive Replaying(int limit)
        {
            return message => message.Match()
                .With<ReplayedAllMessage>(replayed =>
                {
                    var seqNr = replayed.Persistent.SequenceNr;
                    Buffer.Add(new EventEnvelope(
                        offset: seqNr,
                        persistenceId: replayed.Persistent.PersistenceId,
                        sequenceNr: seqNr,
                        @event: replayed.Persistent.Payload));
                    CurrentOffset = replayed.Offset;
                    Buffer.DeliverBuffer(TotalDemand);
                })
                .With<RecoverySuccess>(success =>
                {
                    Log.Debug("replay completed for current offset [{0}]", CurrentOffset);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                })
                .With<ReplayMessagesFailure>(failure =>
                {
                    Log.Debug("replay failed for offset [{0}], due to [{1}]", CurrentOffset, failure.Cause.Message);
                    Buffer.DeliverBuffer(TotalDemand);
                    OnErrorThenStop(failure.Cause);
                })
                .With<Request>(_ => Buffer.DeliverBuffer(TotalDemand))
                .With<EventsByPersistenceIdPublisher.Continue>(() => { }) // skip during replay
                .With<EventAppended>(() => { }) // skip during replay
                .With<Cancel>(_ => Context.Stop(Self))
                .WasHandled;
        }
    }

    internal sealed class LiveAllEventsPublisher : AbstractAllEventsPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveAllEventsPublisher(long fromOffset, long toOffset, int maxBufferSize, string writeJournalPluginId, TimeSpan refreshInterval) 
            : base(fromOffset, toOffset, maxBufferSize, writeJournalPluginId)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, AllEventsPublisher.Continue.Instance, Self);
        }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveInitialRequest()
        {
            JournalRef.Tell(SubscribeAllEvents.Instance);
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

    internal sealed class CurrentAllEventsPublisher : AbstractAllEventsPublisher
    {
        public CurrentAllEventsPublisher(long fromOffset, long toOffset, int maxBufferSize, string writeJournalPluginId) 
            : base(fromOffset, toOffset, maxBufferSize, writeJournalPluginId)
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
                Self.Tell(AllEventsPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (highestSequenceNr < ToOffset)
                _toOffset = highestSequenceNr;

            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(AllEventsPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}