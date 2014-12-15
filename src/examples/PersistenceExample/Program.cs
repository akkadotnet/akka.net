using System;
using Akka.Actor;

namespace PersistenceExample
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("example"))
            {
                BasicUsage(system);

                FailingActorExample(system);

                SnapshotedActor(system);

                ViewExample(system);

                Console.ReadLine();
            }
        }

        private static void ViewExample(ActorSystem system)
        {
            var pref = system.ActorOf(Props.Create<ViewExampleActor>());
            var view = system.ActorOf(Props.Create<ExampleView>());

            system.Scheduler.Schedule(TimeSpan.Zero, TimeSpan.FromSeconds(2), pref, "sheduled");
            system.Scheduler.Schedule(TimeSpan.Zero, TimeSpan.FromSeconds(5), view, "snap");
        }

        private static void SnapshotedActor(ActorSystem system)
        {
            var pref = system.ActorOf(Props.Create<SnapshotedExampleActor>(), "snapshoted-actor");
            pref.Tell("a");
            pref.Tell("b");
            pref.Tell("snap");
            pref.Tell("c");
            pref.Tell("d");
            pref.Tell("print");
        }

        private static void FailingActorExample(ActorSystem system)
        {
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

            // received List(a)
            // received List(a, b)
            // received List(a, b, c)

            // Will print in a second run:

            // received List(a, b, c, a)
            // received List(a, b, c, a, b)
            // received List(a, b, c, a, b, c)

            // etc ...
        }

        private static void BasicUsage(ActorSystem system)
        {
            var aref = system.ActorOf(Props.Create<ExamplePersistentActor>(), "basic-actor");

            aref.Tell(new Command("foo"));
            aref.Tell(new Command("baz"));
            aref.Tell(new Command("bar"));
            aref.Tell("snap");
            aref.Tell(new Command("buzz"));
            aref.Tell("print");
        }
    }
}
