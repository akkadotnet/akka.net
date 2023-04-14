//-----------------------------------------------------------------------
// <copyright file="SnapshotedExampleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Persistence;

namespace PersistenceExample
{
    public class SnapshotedExampleActor : PersistentActor
    {
        public SnapshotedExampleActor()
        {
            State = new ExampleState();
        }

        public override string PersistenceId { get { return "sample-id-3"; } }

        public ExampleState State { get; set; }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer offer:
                    var s = (ExampleState) offer.Snapshot;
                    Console.WriteLine("Offered state (from snapshot): " + s);
                    State = s;
                    return true;
                
                case string _:
                    State = State.Update(new Event(message.ToString()));
                    return true;
                
                default:
                    return false;
            }
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case string str when str == "print":
                    Console.WriteLine("Current actor's state: " + State);
                    return true;
                case string str when str == "snap":
                    SaveSnapshot(State);
                    return true;
                case string str:
                    Persist(str, evt => State = State.Update(new Event(evt)));
                    return true;
                case SaveSnapshotFailure _:
                case SaveSnapshotSuccess _:
                    return true;
                default:
                    return false;
            }
        }
    }
}

