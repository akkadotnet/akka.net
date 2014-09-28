using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;

namespace Samples.Cluster.Simple
{
    class Program
    {
        private static void Main(string[] args)
        {
            StartUp(args.Length == 0 ? new String[] {"2551", "2552", "0"} : args);
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        public static void StartUp(string[] ports)
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            foreach (var port in ports)
            {
                //Override the configuration of the port
                var config =
                    ConfigurationFactory.ParseString("akka.remote.helios.tcp.port=" + port)
                        .WithFallback(section.AkkaConfig);

                //create an Akka system
                var system = ActorSystem.Create("ClusterSystem", config);

                //create an actor that handles cluster domain events
                system.ActorOf(Props.Create(typeof (SimpleClusterListener)), "clusterListener");
            }
        }
    }
}
