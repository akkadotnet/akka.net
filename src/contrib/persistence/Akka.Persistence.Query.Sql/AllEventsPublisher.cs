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
            public static readonly Continue Instance = new();

            private Continue() { }
        }

        public static Props Props(long fromOffset, TimeSpan? refreshInterval, int maxBufferSize, IActorRef writeJournal, IActorRef queryPermitter)
        {
            return refreshInterval.HasValue ?
                Actor.Props.Create(() => new LiveAllEventsPublisher(fromOffset, refreshInterval.Value, maxBufferSize, writeJournal, queryPermitter)) :
                Actor.Props.Create(() => new CurrentAllEventsPublisher(fromOffset, maxBufferSize, writeJournal, queryPermitter));
        }
    }

    internal abstract class AbstractAllEventsPublisher : ActorPublisher<EventEnvelope>
    {

        private ILoggingAdapter _log;
        protected long CurrentOffset;

        protected AbstractAllEventsPublisher(long fromOffset, int maxBufferSize, IActorRef journalRef, IActorRef queryPermitter)
        {
            CurrentOffset = FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            JournalRef = journalRef;
            QueryPermitter = queryPermitter;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
        }

        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        protected IActorRef JournalRef { get; }
        protected IActorRef QueryPermitter { get; }
        protected DeliveryBuffer<EventEnvelope> Buffer { get; }
        protected long FromOffset { get; }
        protected abstract long ToOffset { get; }
        protected int MaxBufferSize { get; }
        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(long highestOrderingNr);

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    ReceiveInitialRequest();
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
                case AllEventsPublisher.Continue _:
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
            Log.Debug("replay all events request from [{0}] to [{1}], limit [{2}]", CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new ReplayAllEvents(CurrentOffset, ToOffset, limit, Self));
            Context.Become(Replaying);
        }

        protected bool Replaying( object message )
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
                    return true;
                case EventReplaySuccess success:
                    Log.Debug("event replay completed, currOffset [{0}], highestSequenceNr [{1}]", CurrentOffset, success.HighestSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;
                case EventReplayFailure failure:
                    Log.Error(failure.Cause, "event replay failed, due to [{0}]", failure.Cause.Message);
                    Buffer.DeliverBuffer(TotalDemand);
                    OnErrorThenStop(failure.Cause);
                    return true;
                case Request _:
                    Buffer.DeliverBuffer(TotalDemand);
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

    }

    internal sealed class LiveAllEventsPublisher : AbstractAllEventsPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveAllEventsPublisher(long fromOffset, TimeSpan refreshInterval, int maxBufferSize, IActorRef writeJournal, IActorRef queryPermitter)
            : base(fromOffset, maxBufferSize, writeJournal, queryPermitter)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, AllEventsPublisher.Continue.Instance, Self);
        }

        protected override long ToOffset => long.MaxValue;

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
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestOrderingNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentAllEventsPublisher : AbstractAllEventsPublisher
    {
        public CurrentAllEventsPublisher(long fromOffset, int maxBufferSize, IActorRef writeJournal, IActorRef queryPermitter)
            : base(fromOffset, maxBufferSize, writeJournal, queryPermitter)
        { }

        private long _toOffset = long.MaxValue;
        protected override long ToOffset => _toOffset;

        protected override void ReceiveInitialRequest()
        {
            RequestQueryPermit();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
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
