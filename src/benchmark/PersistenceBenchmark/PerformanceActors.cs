//-----------------------------------------------------------------------
// <copyright file="PerformanceActors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Persistence;
using Akka.Actor;

namespace PersistenceBenchmark
{
    public abstract class PerformanceTestActorBase : PersistentActor
    {
        private readonly string _persistenceId;

        protected long FailAt { get; set; }

        protected PerformanceTestActorBase(string persistenceId)
        {
            _persistenceId = persistenceId;
        }

        public sealed override string PersistenceId { get { return _persistenceId; } }

        protected sealed override bool ReceiveRecover(object message)
        {
            if (LastSequenceNr % 1000 == 0) ;

            return true;
        }

        protected bool ControlBehavior(object message)
        {
            var sender = Sender;
            if (message is StopMeasure) Defer(StopMeasure.Instance, _ => sender.Tell(StopMeasure.Instance));
            else if (message is FailAt) FailAt = ((FailAt)message).SequenceNr;
            else return false;
            return true;
        }
    }

    public sealed class CommandsourcedPersistentActor : PerformanceTestActorBase
    {
        public CommandsourcedPersistentActor(string persistenceId) : base(persistenceId)
        {
        }

        protected override bool ReceiveCommand(object message)
        {
            if (!ControlBehavior(message))
            {
                PersistAsync(message, e =>
                {
                    if (LastSequenceNr % 1000 == 0) ;
                    if (LastSequenceNr == FailAt) throw new PerformanceTestException("boom");
                });
            }
            return true;
        }
    }

    public sealed class EventsourcedPersistentActor : PerformanceTestActorBase
    {
        public EventsourcedPersistentActor(string persistenceId) : base(persistenceId)
        {
        }

        protected override bool ReceiveCommand(object message)
        {
            if (!ControlBehavior(message))
            {
                Persist(message, e =>
                {
                    if (LastSequenceNr % 1000 == 0) ;
                    if (LastSequenceNr == FailAt) throw new PerformanceTestException("boom");
                });
            }
            return true;
        }
    }

    public sealed class MixedPersistentActor : PerformanceTestActorBase
    {
        private int counter = 0;
        public MixedPersistentActor(string persistenceId) : base(persistenceId)
        {
        }

        private void Handler(object message)
        {
            if (LastSequenceNr % 1000 == 0) ;
            if (LastSequenceNr == FailAt) throw new PerformanceTestException("boom");
        }

        protected override bool ReceiveCommand(object message)
        {
            if (!ControlBehavior(message))
            {
                counter++;
                if (counter % 10 == 0) Persist(message, Handler);
                else PersistAsync(message, Handler);
            }
            return true;
        }
    }

    public sealed class StashingEventsourcedPersistentActor : PerformanceTestActorBase
    {
        public StashingEventsourcedPersistentActor(string persistenceId) : base(persistenceId)
        {
        }

        private object PrintProgress(object message)
        {
            if (LastSequenceNr % 1000 == 0) Console.Write(".");
            return message;
        }

        private bool ProcessC(object message)
        {
            PrintProgress(message);
            if (object.Equals("c", message))
            {
                var context = Context;
                Persist("c", _ => context.UnbecomeStacked());
                UnstashAll();
            }
            else Stash.Stash();
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            PrintProgress(message);
            if (!ControlBehavior(message))
            {
                var context = Context;
                if (object.Equals("a", message))
                    Persist("a", _ => context.BecomeStacked(ProcessC));
                else if (object.Equals("b", message))
                    Persist("b", s => { });
                else return false;
            }
            return true;
        }
    }
}