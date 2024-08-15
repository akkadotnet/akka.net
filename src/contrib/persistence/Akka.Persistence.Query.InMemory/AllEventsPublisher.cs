﻿//-----------------------------------------------------------------------
// <copyright file="AllEventsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.InMemory
{
    internal static class AllEventsPublisher
    {
        [Serializable]
        public sealed class Continue
        {
            public static readonly Continue Instance = new();

            private Continue() { }
        }

        public static Props Props(int fromOffset, TimeSpan? refreshInterval, int maxBufferSize, string writeJournalPluginId)
        {
            return refreshInterval.HasValue ?
                Actor.Props.Create(() => new LiveAllEventsPublisher(fromOffset, refreshInterval.Value, maxBufferSize, writeJournalPluginId)) :
                Actor.Props.Create(() => new CurrentAllEventsPublisher(fromOffset, maxBufferSize, writeJournalPluginId));
        }
    }

    internal abstract class AbstractAllEventsPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;
        protected int CurrentOffset;

        protected AbstractAllEventsPublisher(int fromOffset, int maxBufferSize, string writeJournalPluginId)
        {
            CurrentOffset = FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        protected IActorRef JournalRef { get; }
        protected DeliveryBuffer<EventEnvelope> Buffer { get; }
        protected int FromOffset { get; }
        protected abstract int ToOffset { get; }
        protected int MaxBufferSize { get; }
        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected abstract void ReceiveInitialRequest();
        protected abstract void ReceiveIdleRequest();
        protected abstract void ReceiveRecoverySuccess(int highestOrderingNr);

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
            Log.Debug("replay all events request from [{0}] to [{1}], limit [{2}]", CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new MemoryJournal.ReplayAllEvents(CurrentOffset, ToOffset, limit, Self));
            Context.Become(Replaying);
        }

        protected bool Replaying( object message)
        {
            switch (message)
            {
                case MemoryJournal.ReplayedEvent replayed:
                    // ReplayEvent might overshoot the current ToOffset target
                    if (replayed.Offset > ToOffset)
                        return true;

                    // NOTES: tags is empty because tags are not retrieved from the database query (as of this writing)
                    Buffer.Add(new EventEnvelope(
                        offset: new Sequence(replayed.Offset),
                        persistenceId: replayed.Persistent.PersistenceId,
                        sequenceNr: replayed.Persistent.SequenceNr,
                        @event: replayed.Persistent.Payload,
                        timestamp: replayed.Persistent.Timestamp,
                        tags: Array.Empty<string>()));

                    CurrentOffset = replayed.Offset + 1;
                    Buffer.DeliverBuffer(TotalDemand);
                    return true;
                case MemoryJournal.EventReplaySuccess success:
                    Log.Debug("event replay completed, currOffset [{0}], highestSequenceNr [{1}]", CurrentOffset, success.HighestSequenceNr);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                    return true;
                case MemoryJournal.EventReplayFailure failure:
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
        public LiveAllEventsPublisher(int fromOffset, TimeSpan refreshInterval, int maxBufferSize, string writeJournalPluginId)
            : base(fromOffset, maxBufferSize, writeJournalPluginId)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, AllEventsPublisher.Continue.Instance, Self);
        }

        protected override int ToOffset => int.MaxValue;

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

        protected override void ReceiveRecoverySuccess(int highestOrderingNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentAllEventsPublisher : AbstractAllEventsPublisher
    {
        public CurrentAllEventsPublisher(int fromOffset, int maxBufferSize, string writeJournalPluginId)
            : base(fromOffset, maxBufferSize, writeJournalPluginId)
        { }

        private int _toOffset = int.MaxValue;
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
                Self.Tell(AllEventsPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(int highestOrderingNr)
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
