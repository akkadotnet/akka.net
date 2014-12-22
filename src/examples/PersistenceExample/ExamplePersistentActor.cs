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

        public string Data { get; private set; }
    }

    public class Event
    {
        public Event(string data)
        {
            Data = data;
        }

        public string Data { get; private set; }
    }

    public class ExampleState
    {
        public ExampleState(List<string> events = null)
        {
            Events = events ?? new List<string>();
        }

        public IEnumerable<string> Events { get; private set; }

        public ExampleState Update(Event evt)
        {
            var list = new[] { evt.Data }.Union(Events).ToList();
            return new ExampleState(list);
        }

        public override string ToString()
        {
            return string.Join("; ", Events);
        }
    }

    public class ExamplePersistentActor : PersistentActor
    {
        public ExamplePersistentActor()
        {
            State = new ExampleState();
        }

        public override string PersistenceId { get { return "sample-id-1"; }}

        public ExampleState State { get; set; }
        public int EventsCount { get { return State.Events.Count(); } }

        public void UpdateState(Event evt)
        {
            State = State.Update(evt);
        }

        protected override bool ReceiveRecover(object message)
        {
            ExampleState state;
            if (message is Event)
                UpdateState(message as Event);
            else if (message is SnapshotOffer && (state = ((SnapshotOffer) message).Snapshot as ExampleState) != null)
                State = state;
            else return false;
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message is Command)
            {
                var cmd = message as Command;
                Persist(new Event(cmd.Data + "-" + EventsCount), UpdateState);
                Persist(new Event(cmd.Data + "-" + EventsCount+1), @event =>
                {
                    UpdateState(@event);
                    Context.System.EventStream.Publish(@event);
                });
            }
            else if (message == "snap")
                SaveSnapshot(State);
            else if (message == "print")
                Console.WriteLine(State);
            else return false;
            return true;
        }
    }
}