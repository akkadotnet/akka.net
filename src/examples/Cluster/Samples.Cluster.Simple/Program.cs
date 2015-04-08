//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Configuration;
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
