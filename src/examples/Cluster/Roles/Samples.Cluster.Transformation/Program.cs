//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Configuration;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Util.Internal;

namespace Samples.Cluster.Transformation
{
    class Program
    {
        private static Config _clusterConfig;

        static void Main(string[] args)
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            _clusterConfig = section.AkkaConfig;
            LaunchBackend(new []{ "2551" });
            LaunchBackend(new[] { "2552" });
            LaunchBackend(new string[0]);
            LaunchFrontend(new string[0]);
            LaunchFrontend(new string[0]);
            //starting 2 frontend nodes and 3 backend nodes
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        static void LaunchBackend(string[] args)
        {
            var port = args.Length > 0 ? args[0] : "0";
            var config =
                    ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + port)
                    .WithFallback(ConfigurationFactory.ParseString("akka.cluster.roles = [backend]"))
                        .WithFallback(_clusterConfig);

            var system = ActorSystem.Create("ClusterSystem", config);
            system.ActorOf(Props.Create<TransformationBackend>(), "backend");
        }

        static void LaunchFrontend(string[] args)
        {
            var port = args.Length > 0 ? args[0] : "0";
            var config =
                    ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + port)
                    .WithFallback(ConfigurationFactory.ParseString("akka.cluster.roles = [frontend]"))
                        .WithFallback(_clusterConfig);

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

