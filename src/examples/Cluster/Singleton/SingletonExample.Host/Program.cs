using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using SingletonExample.Shared;

namespace SingletonExample.Host
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("singleton-cluster-system"))
            {
                var aref = system.ActorOf(ClusterSingletonManager.Props(
                    singletonProps: Props.Create(() => new WorkerManager()),
                    terminationMessage: PoisonPill.Instance,
                    settings: ClusterSingletonManagerSettings.Create(system)),
                    name: "manager");



                Console.ReadLine();
            }
        }
    }
}
