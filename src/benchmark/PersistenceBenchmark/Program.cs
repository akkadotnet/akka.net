//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace PersistenceBenchmark
{
    class Program
    {
        // if you want to benchmark your persistent storage provides, paste the configuration in string below
        // by default we're checking against in-memory journal
        private static Config config = ConfigurationFactory.ParseString(@"
            ");

        public const int LoadCycles = 1000;

        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("persistent-benchmark", config))
            {
                StressCommandsourcedActor(system, null);
                StressEventsourcedActor(system, null);
            }

            Console.ReadLine();
        }

        private static void StressCommandsourcedActor(ActorSystem system, long? failAt)
        {
            var pref = system.ActorOf(Props.Create(() => new CommandsourcedPersistentActor("commandsourced-1")));
            StressPersistentActor(pref, failAt, "persistent commands");
        }

        private static void StressEventsourcedActor(ActorSystem system, long? failAt)
        {
            var pref = system.ActorOf(Props.Create(() => new EventsourcedPersistentActor("eventsourced-1")));
            StressPersistentActor(pref, failAt, "persistent events");
        }

        private static void StressMixedActor(ActorSystem system, long? failAt)
        {
            var pref = system.ActorOf(Props.Create(() => new MixedPersistentActor("mixed-1")));
            StressPersistentActor(pref, failAt, "persistent events and commands");
        }

        private static void StressStashingPersistentActor(ActorSystem system)
        {
            var pref = system.ActorOf(Props.Create(() => new StashingEventsourcedPersistentActor("stashing-1")));
            var m = new Measure(LoadCycles);

            var commands = Enumerable.Range(1, LoadCycles/3).SelectMany(_ => new[] {"a", "b", "c"}).ToArray();
            m.StartMeasure();

            foreach (var command in commands) pref.Tell(command);

            pref.Ask(StopMeasure.Instance, TimeSpan.FromSeconds(100)).Wait();
            var ratio = m.StopMeasure();
            Console.WriteLine("Throughtput: {0} persisted events per second", ratio);
        }

        private static void StressPersistentActor(IActorRef pref, long? failAt, string description)
        {
            if (failAt.HasValue) pref.Tell(new FailAt(failAt.Value));

            var m = new Measure(LoadCycles);
            m.StartMeasure();

            for (int i = 1; i <= LoadCycles; i++) pref.Tell("msg" + i);

            pref.Ask(StopMeasure.Instance, TimeSpan.FromSeconds(100)).Wait();
            var ratio = m.StopMeasure();
            Console.WriteLine("Throughtput: {0} {1} per second", ratio, description);
        }
    }
}
