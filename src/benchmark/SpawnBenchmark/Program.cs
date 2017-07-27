using Akka.Actor;
using Akka.Configuration;
using System;
using System.Runtime;

namespace SpawnBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine($"Is Server GC {GCSettings.IsServerGC}");

            var config = ConfigurationFactory.ParseString("akka.suppress-json-serializer-warning=on");
            using (var sys = ActorSystem.Create("main", config))
            {
                var actor = sys.ActorOf(RootActor.Props);
                actor.Tell(new RootActor.Run(5));
                Console.ReadLine();
            }
        }
    }
}