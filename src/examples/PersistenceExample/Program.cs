﻿using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence;

namespace PersistenceExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString("akka.actor.logLevel = DEBUG")
                .WithFallback(Persistence.DefaultConfig());

            using (var system = ActorSystem.Create("example",  config))
            {
                //BasicUsage(system);

                // FailingActorExample(system);

                SnapshotedActor(system);

                //ViewExample(system);

                Console.ReadLine();
            }
        }

        private static void ViewExample(ActorSystem system)
        {
            Console.WriteLine("\n--- PERSISTENT VIEW EXAMPLE ---\n");
            var pref = system.ActorOf(Props.Create<ViewExampleActor>());
            var view = system.ActorOf(Props.Create<ExampleView>());

            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromSeconds(2), pref, "scheduled", ActorRef.NoSender);
            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromSeconds(5), view, "snap", ActorRef.NoSender);
        }

        private static void SnapshotedActor(ActorSystem system)
        {
            Console.WriteLine("\n--- SNAPSHOTED ACTOR EXAMPLE ---\n");
            var pref = system.ActorOf(Props.Create<SnapshotedExampleActor>(), "snapshoted-actor");

            // send two messages (a, b) and persist them
            pref.Tell("a");
            pref.Tell("b");

            // make a snapshot: a, b will be stored in durable memory
            pref.Tell("snap");

            // send next two messages - those will be cleared, since MemoryJournal is not "persistent"
            pref.Tell("c");
            pref.Tell("d");

            // print internal actor's state
            pref.Tell("print");

            // result after first run should be like:

            // Current actor's state: d, c, b, a

            // after second run:

            // Offered state (from snapshot): b, a      - taken from the snapshot
            // Current actor's state: d, c, b, a, b, a  - 2 last messages loaded from the snapshot, rest send in this run

            // after third run:

            // Offered state (from snapshot): b, a, b, a        - taken from the snapshot
            // Current actor's state: d, c, b, a, b, a, b, a    - 4 last messages loaded from the snapshot, rest send in this run

            // etc...
        }

        private static void FailingActorExample(ActorSystem system)
        {
            Console.WriteLine("\n--- FAILING ACTOR EXAMPLE ---\n");
            var pref = system.ActorOf(Props.Create<ExamplePersistentFailingActor>(), "failing-actor");

            pref.Tell("a");
            pref.Tell("print");
            // restart and recovery
            pref.Tell("boom");
            pref.Tell("print");
            pref.Tell("b");
            pref.Tell("print");
            pref.Tell("c");
            pref.Tell("print");

            // Will print in a first run (i.e. with empty journal):

            // Received: a
            // Received: a, b
            // Received: a, b, c
        }

        private static void BasicUsage(ActorSystem system)
        {
            Console.WriteLine("\n--- BASIC EXAMPLE ---\n");

            // create a persistent actor, using LocalSnapshotStore and MemoryJournal
            var aref = system.ActorOf(Props.Create<ExamplePersistentActor>(), "basic-actor");

            // all commands are stacked in internal actor's state as a list
            aref.Tell(new Command("foo"));
            aref.Tell(new Command("baz"));
            aref.Tell(new Command("bar"));

            // save current actor state using LocalSnapshotStore (it will be serialized and stored inside file on example bin/snapshots folder)
            aref.Tell("snap");

            // add one more message, this one is not snapshoted and won't be persisted (because of MemoryJournal characteristics)
            aref.Tell(new Command("buzz"));

            // print current actor state
            aref.Tell("print");

            // on first run displayed state should be: 
            
            // buzz-3, bar-2, baz-1, foo-0 
            // (numbers denotes current actor's sequence numbers for each stored event)

            // on the second run: 

            // buzz-6, bar-5, baz-4, foo-3, bar-2, baz-1, foo-0
            // (sequence numbers are continuously increasing taken from last snapshot, 
            // also buzz-3 event isn't present since it's has been called after snapshot request,
            // and MemoryJournal will destroy stored events on program stop)

            // on next run's etc...
        }
    }
}
