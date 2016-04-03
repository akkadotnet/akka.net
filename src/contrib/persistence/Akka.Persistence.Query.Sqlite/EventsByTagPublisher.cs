﻿//-----------------------------------------------------------------------
// <copyright file="EventsByTagPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sqlite
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

        public static Props Props(string tag, long fromOffset, long toOffset, TimeSpan? refreshInterval, int maxBufferSize, string writeJournalPluginId)
        {
            return refreshInterval.HasValue
                ? Actor.Props.Create(() => new LiveEventsByTagPublisher(tag, fromOffset, toOffset, refreshInterval.Value, maxBufferSize, writeJournalPluginId))
                : Actor.Props.Create(() => new CurrentEventsByTagPublisher(tag, fromOffset, toOffset, maxBufferSize, writeJournalPluginId));
        }
    }

    internal abstract class AbstractEventsByTagPublisher : ActorPublisher<EventEnvelope>
    {
        private ILoggingAdapter _log;

        protected ImmutableArray<EventEnvelope> Buffer = ImmutableArray<EventEnvelope>.Empty;
        protected readonly IActorRef JournalRef;
        protected long CurrentOffset;
        protected AbstractEventsByTagPublisher(string tag, long fromOffset, int maxBufferSize, string writeJournalPluginId)
        {
            Tag = tag;
            FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            WriteJournalPluginId = writeJournalPluginId;

            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());
        protected string Tag { get; }
        protected long FromOffset { get; }
        protected abstract long ToOffset { get; }
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

        protected bool Init(object message) => message.Match()
            .With<Request>(_ => ReceiveInitialRequest())
            .With<EventsByTagPublisher.Continue>(() => { })
            .With<Cancel>(_ => Context.Stop(Self))
            .WasHandled;

        protected bool Idle(object message) => message.Match()
            .With<EventsByTagPublisher.Continue>(() =>
            {
                if (IsTimeForReplay) Replay();
            })
            .With<TaggedEventAppended>(() =>
            {
                if (IsTimeForReplay) Replay();
            })
            .With<Request>(ReceiveIdleRequest)
            .With<Cancel>(() => Context.Stop(Self))
            .WasHandled;

        protected void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("request replay for tag [{0}] from [{1}] to [{2}] limit [{3}]", Tag, CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new ReplayTaggedMessages(CurrentOffset, ToOffset, limit, Tag, Self));
            Context.Become(Replaying(limit));
        }

        protected Receive Replaying(int limit)
        {
            return message => message.Match()
                .With<ReplayedTaggedMessage>(replayed =>
                {
                    Buffer = Buffer.Add(new EventEnvelope(
                        offset: replayed.Offset,
                        persistenceId: replayed.Persistent.PersistenceId,
                        sequenceNr: replayed.Persistent.SequenceNr,
                        @event: replayed.Persistent.Payload));

                    CurrentOffset = replayed.Offset + 1;
                    DeliverBuffer();
                })
                .With<RecoverySuccess>(success =>
                {
                    Log.Debug("replay completed for tag [{0}], currOffset [{1}]", Tag, CurrentOffset);
                    ReceiveRecoverySuccess(success.HighestSequenceNr);
                })
                .With<ReplayMessagesFailure>(failure =>
                {
                    Log.Debug("replay failed for tag [{0}], due to [{1}]", Tag, failure.Cause.Message);
                    DeliverBuffer();
                    OnErrorThenStop(failure.Cause);
                })
                .With<Request>(_ => DeliverBuffer())
                .With<EventsByTagPublisher.Continue>(() => { })
                .With<TaggedEventAppended>(() => { })
                .With<Cancel>(() => Context.Stop(Self))
                .WasHandled;
        }

        protected void DeliverBuffer()
        {
            if (!Buffer.IsEmpty && TotalDemand > 0)
            {
                var totalDemand = (int)TotalDemand;
                if (Buffer.Length == 1)
                {
                    // optimize for this common case
                    OnNext(Buffer[0]);
                    Buffer = ImmutableArray<EventEnvelope>.Empty;
                }
                else if (totalDemand <= int.MaxValue)
                {
                    for (var i = 0; i < totalDemand; i++)
                        OnNext(Buffer[i]);

                    Buffer = Buffer.RemoveRange(0, totalDemand);
                }
                else
                {
                    foreach (var element in Buffer)
                        OnNext(element);

                    Buffer = ImmutableArray<EventEnvelope>.Empty;
                }
            }
        }
    }

    internal sealed class LiveEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveEventsByTagPublisher(string tag, long fromOffset, long toOffset, TimeSpan refreshInterval, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
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
            JournalRef.Tell(new SubscribeTag(Tag));
            Replay();
        }

        protected override void ReceiveIdleRequest()
        {
            DeliverBuffer();
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            DeliverBuffer();
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        public CurrentEventsByTagPublisher(string tag, long fromOffset, long toOffset, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
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
            DeliverBuffer();
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            DeliverBuffer();
            if (highestSequenceNr < ToOffset)
                _toOffset = highestSequenceNr;
            else if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance);

            Context.Become(Idle);
        }
    }
}