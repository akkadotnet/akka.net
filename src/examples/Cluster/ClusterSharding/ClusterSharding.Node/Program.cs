using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using ClusterSharding.Node.AutomaticJoin;

namespace ClusterSharding.Node
{
    using ClusterSharding = Akka.Cluster.Sharding.ClusterSharding;

    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("sharded-cluster-system", ConfigurationFactory.Load().WithFallback(ClusterSingletonManager.DefaultConfig())))
            {
                var automaticCluster = new AutomaticCluster(system);
                try
                {
                    automaticCluster.Join();

                    RunExample(system);

                    Console.ReadLine();
                }
                finally
                {
                    // you may need to remove SQLite database file from bin/Debug or bin/Release in case when unexpected crash happened
                    automaticCluster.Leave();
                }
            }
        }

        private static void RunExample(ActorSystem system)
        {
            var sharding = ClusterSharding.Get(system);
            var shardRegion = sharding.Start(
                typeName: "printer",
                entityProps: Props.Create<Printer>(),
                settings: ClusterShardingSettings.Create(system),
                messageExtractor: new MessageExtractor());

            Thread.Sleep(5000);
            Console.Write("Press ENTER to start producing messages...");
            Console.ReadLine();

            ProduceMessages(system, shardRegion);
        }

        private static void ProduceMessages(ActorSystem system, IActorRef shardRegion)
        {
            const int entitiesCount = 20;
            const int shardsCount = 10;
            var rand = new Random();

            system.Scheduler.Advanced.ScheduleRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    var shardId = rand.Next(shardsCount);
                    var entityId = rand.Next(entitiesCount);

                    shardRegion.Tell(new Printer.Env(shardId, entityId, "hello world"));
                }
            });
        }
    }
}
