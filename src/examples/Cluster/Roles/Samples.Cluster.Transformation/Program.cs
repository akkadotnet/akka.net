//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;

namespace Samples.Cluster.Transformation
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
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }

              cluster {
                seed-nodes = [
                  ""akka.tcp://ClusterSystem@127.0.0.1:2551"",
                  ""akka.tcp://ClusterSystem@127.0.0.1:2552""]
              }
            }");

        static void Main(string[] args)
        {
            LaunchBackend(new []{ "2551" });
            LaunchBackend(new[] { "2552" });
            LaunchBackend(Array.Empty<string>());
            LaunchFrontend(Array.Empty<string>());
            LaunchFrontend(Array.Empty<string>());
            //starting 2 frontend nodes and 3 backend nodes
            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();
        }

        static void LaunchBackend(string[] args)
        {
            var port = args.Length > 0 ? args[0] : "0";
            var config =
                    ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + port)
                    .WithFallback(ConfigurationFactory.ParseString("akka.cluster.roles = [backend]"))
                        .WithFallback(ClusterConfig);

            var system = ActorSystem.Create("ClusterSystem", config);
            system.ActorOf(Props.Create<TransformationBackend>(), "backend");
        }

        static void LaunchFrontend(string[] args)
        {
            var port = args.Length > 0 ? args[0] : "0";
            var config =
                    ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + port)
                    .WithFallback(ConfigurationFactory.ParseString("akka.cluster.roles = [frontend]"))
                        .WithFallback(ClusterConfig);

            var system = ActorSystem.Create("ClusterSystem", config);

            var frontend = system.ActorOf(Props.Create<TransformationFrontend>(), "frontend");
            var interval = TimeSpan.FromSeconds(2);
            var timeout = TimeSpan.FromSeconds(5);
            var counter = new AtomicCounter();
            system.Scheduler.Advanced.ScheduleRepeatedly(interval, interval,
                () => frontend.Ask(new TransformationMessages.TransformationJob("hello-" + counter.GetAndIncrement()), timeout)
                    .ContinueWith(
                        r => Console.WriteLine(r.Result)));
        }
    }
}
