//-----------------------------------------------------------------------
// <copyright file="ExamplePersistentActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Persistence;

namespace PersistenceExample
{
    public class Command
    {
        public Command(string data)
        {
            Data = data;
        }

        public string Data { get; }

        public override string ToString()
            => Data;
    }

    public class Event
    {
        public Event(string data)
        {
            Data = data;
        }

        public string Data { get; }

        public override string ToString()
            => Data;
    }

    public class ExampleState
    {
        public ExampleState(List<string> events = null)
        {
            Events = events ?? new List<string>();
        }

        public IEnumerable<string> Events { get; }

        public ExampleState Update(Event evt)
        {
            var list = new List<string> {evt.Data};
            list.AddRange(Events);
            return new ExampleState(list);
        }

        public override string ToString()
        {
            return string.Join(", ", Events);
        }
    }

    public class ExamplePersistentActor : PersistentActor
    {
        public ExamplePersistentActor()
        {
            State = new ExampleState();
        }

        public override string PersistenceId { get { return "sample-id-1"; }}

        private ExampleState State { get; set; }

        private int EventsCount
        {
            get => State.Events.Count();
        }

        private void UpdateState(Event evt)
        {
            State = State.Update(evt);
        }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case Event @event:
                    UpdateState(@event);
                    return true;
                case SnapshotOffer { Snapshot: ExampleState state }:
                    State = state;
                    return true;
                default:
                    return false;
            }
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case Command cmd:
                    Persist(new Event(cmd.Data + "-" + EventsCount), UpdateState);
                    return true;
                case string msg when msg == "snap":
                    SaveSnapshot(State);
                    return true;
                case string msg when msg == "print":
                    Console.WriteLine(State);
                    return true;
                case SaveSnapshotSuccess _:
                case SaveSnapshotFailure _:
                    return true;
                default:
                    return false;
            }
        }
    }
}

