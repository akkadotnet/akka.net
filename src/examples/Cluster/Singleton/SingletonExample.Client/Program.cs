using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using SingletonExample.Shared;

namespace SingletonExample.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("singleton-cluster-system"))
            {
                var proxyRef = system.ActorOf(ClusterSingletonProxy.Props(
                    singletonManagerPath: "/user/manager",
                    settings: ClusterSingletonProxySettings.Create(system).WithRole("worker")),
                    name: "managerProxy");



                Console.ReadLine();
            }
        }
    }
}
