// //-----------------------------------------------------------------------
// // <copyright file="CurrentPersistenceIdsPublisher.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.InMemory
{
    internal class CurrentPersistenceIdsPublisher : ActorPublisher<string>, IWithUnboundedStash
    {
        public static Props Props(string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new CurrentPersistenceIdsPublisher(writeJournalPluginId));
        }

        private readonly IActorRef _journalRef;

        private readonly DeliveryBuffer<string> _buffer;
        private readonly ILoggingAdapter _log;

        public IStash Stash { get; set; }

        public CurrentPersistenceIdsPublisher(string writeJournalPluginId)
        {
            _buffer = new DeliveryBuffer<string>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
            _log = Context.GetLogger();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    Become(Initializing);
                    _journalRef
                        .Ask<MemoryJournal.CurrentPersistenceIds>(new MemoryJournal.SelectCurrentPersistenceIds(Self))
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
                case MemoryJournal.CurrentPersistenceIds current:
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
                        .Ask<MemoryJournal.CurrentPersistenceIds>(new MemoryJournal.SelectCurrentPersistenceIds(Self))
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
                case MemoryJournal.CurrentPersistenceIds _:
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
}