//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Samples.Cluster.Simple
{
    class Program
    {
        private static readonly Config ClusterConfig = ConfigurationFactory.ParseString(@"
            akka {
              actor {
                provider = cluster
              }

              remote {
                log-remote-lifecycle-events = DEBUG
                dot-netty.tcp {
                  hostname = ""localhost""
                  port = 0
                }
              }

              cluster {
                seed-nodes = [
                  ""akka.tcp://ClusterSystem@localhost:2551"",
                  ""akka.tcp://ClusterSystem@localhost:2552""]
              }
            }");

        private static void Main(string[] args)
        {
            StartUp(args.Length == 0 ? new String[] { "2551", "2552", "0" } : args);
            Console.WriteLine("Press any key to exit");
            Console.ReadLine();
        }

        public static void StartUp(string[] ports)
        {
            foreach (var port in ports)
            {
                //Override the configuration of the port
                var config =
                    ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + port)
                        .WithFallback(ClusterConfig);

                //create an Akka system
                var system = ActorSystem.Create("ClusterSystem", config);

                //create an actor that handles cluster domain events
                system.ActorOf(Props.Create(typeof(SimpleClusterListener)), "clusterListener");
            }
        }
    }
}
