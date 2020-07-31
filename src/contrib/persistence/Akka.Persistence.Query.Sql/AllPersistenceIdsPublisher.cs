//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sql
{
    internal sealed class CurrentPersistenceIdsPublisher : ActorPublisher<string>, IWithUnboundedStash
    {
        public static Props Props(string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new CurrentPersistenceIdsPublisher(writeJournalPluginId));
        }

        private readonly IActorRef _journalRef;

        private readonly DeliveryBuffer<string> _buffer;

        public IStash Stash { get; set; }

        public CurrentPersistenceIdsPublisher(string writeJournalPluginId)
        {
            _buffer = new DeliveryBuffer<string>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    _journalRef.Tell(new SelectCurrentPersistenceIds(0, Self));
                    Become(Initializing);
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        private bool Initializing(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds current:
                    _buffer.AddRange(current.AllPersistenceIds);
                    _buffer.DeliverBuffer(TotalDemand);

                    if (_buffer.IsEmpty)
                    {
                        OnCompleteThenStop();
                        return true;
                    }

                    Become(Active);
                    Stash.UnstashAll();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    if (_buffer.IsEmpty)
                        OnCompleteThenStop();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }
    }

    internal sealed class LivePersistenceIdsPublisher : ActorPublisher<string>, IWithUnboundedStash
    {
        private class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue() { }
        }

        public static Props Props(TimeSpan refreshInterval, string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new LivePersistenceIdsPublisher(refreshInterval, writeJournalPluginId));
        }

        private long _lastOrderingOffset;
        private readonly ICancelable _tickCancelable;
        private readonly IActorRef _journalRef;
        private readonly DeliveryBuffer<string> _buffer;

        public IStash Stash { get; set; }

        public LivePersistenceIdsPublisher(TimeSpan refreshInterval, string writeJournalPluginId)
        {
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                refreshInterval, 
                refreshInterval, 
                Self, 
                Continue.Instance, 
                Self);
            _buffer = new DeliveryBuffer<string>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    _journalRef.Tell(new SelectCurrentPersistenceIds(0, Self));
                    Become(Initializing);
                    return true;
                case Continue _:
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        private bool Initializing(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds current:
                    _lastOrderingOffset = current.HighestOrderingNumber;
                    _buffer.AddRange(current.AllPersistenceIds);
                    _buffer.DeliverBuffer(TotalDemand);

                    Become(Active);
                    Stash.UnstashAll();
                    return true;
                case Continue _:
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case CurrentPersistenceIds added:
                    _lastOrderingOffset = added.HighestOrderingNumber;
                    _buffer.AddRange(added.AllPersistenceIds);
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                case Continue _:
                    _journalRef.Tell(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self));
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }
    }
}
