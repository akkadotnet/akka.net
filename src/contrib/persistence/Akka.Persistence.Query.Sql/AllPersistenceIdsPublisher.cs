//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsPublisher.cs" company="Akka.NET Project">
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
    internal sealed class CurrentPersistenceIdsPublisher : ActorPublisher<string>, IWithUnboundedStash
    {
        public static Props Props(IActorRef writeJournal)
        {
            return Actor.Props.Create(() => new CurrentPersistenceIdsPublisher(writeJournal));
        }

        private readonly IActorRef _journalRef;

        private readonly DeliveryBuffer<string> _buffer;
        private readonly ILoggingAdapter _log;

        public IStash Stash { get; set; }

        public CurrentPersistenceIdsPublisher(IActorRef journalRef)
        {
            _journalRef = journalRef;
            _buffer = new DeliveryBuffer<string>(OnNext);
            _log = Context.GetLogger();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Become(Initializing);
                    _journalRef
                        .Ask<CurrentPersistenceIds>(new SelectCurrentPersistenceIds(0, Self))
                        .PipeTo(Self);
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
                
                case Status.Failure msg:
                    if (msg.Cause is AskTimeoutException e)
                    {
                        _log.Info(e, "Current persistence id query timed out, retrying");
                    }
                    else
                    {
                        _log.Info(msg.Cause, "Current persistence id query failed, retrying");
                    }
                    
                    _journalRef
                        .Ask<CurrentPersistenceIds>(new SelectCurrentPersistenceIds(0, Self))
                        .PipeTo(Self);
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
                case CurrentPersistenceIds _:
                    // Ignore duplicate CurrentPersistenceIds response
                    return true;
                
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
        private sealed class Continue
        {
            public static readonly Continue Instance = new Continue();

            private Continue() { }
        }

        public static Props Props(TimeSpan refreshInterval, IActorRef writeJournal)
        {
            return Actor.Props.Create(() => new LivePersistenceIdsPublisher(refreshInterval, writeJournal));
        }

        private long _lastOrderingOffset;
        private readonly ICancelable _tickCancelable;
        private readonly IActorRef _journalRef;
        private readonly DeliveryBuffer<string> _buffer;
        private readonly ILoggingAdapter _log;

        public IStash Stash { get; set; }

        public LivePersistenceIdsPublisher(TimeSpan refreshInterval, IActorRef journalRef)
        {
            _journalRef = journalRef;
            _log = Context.GetLogger();
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                refreshInterval, 
                refreshInterval, 
                Self, 
                Continue.Instance, 
                Self);
            _buffer = new DeliveryBuffer<string>(OnNext);
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
                    Become(Waiting);
                    _journalRef
                        .Ask<CurrentPersistenceIds>(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self))
                        .PipeTo(Self);
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

        private bool Waiting(object message)
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
                
                case Status.Failure msg:
                    if (msg.Cause is AskTimeoutException e)
                    {
                        _log.Info(e, $"Current persistence id query timed out, retrying. Offset: {_lastOrderingOffset}");
                    }
                    else
                    {
                        _log.Info(msg.Cause, $"Current persistence id query failed, retrying. Offset: {_lastOrderingOffset}");
                    }
                    
                    Become(Active);
                    Stash.UnstashAll();
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
                case CurrentPersistenceIds _:
                    // Ignore duplicate CurrentPersistenceIds response
                    return true;
                
                case Request _:
                    _buffer.DeliverBuffer(TotalDemand);
                    return true;
                
                case Continue _:
                    Become(Waiting);
                    _journalRef
                        .Ask<CurrentPersistenceIds>(new SelectCurrentPersistenceIds(_lastOrderingOffset, Self))
                        .PipeTo(Self);
                    return true;
                
                case Cancel _:
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure msg:
                    _log.Info(msg.Cause, "Unexpected failure received");
                    return true;
                
                default:
                    return false;
            }
        }
    }
}
