//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Samples.Cluster.Metrics.Common;

namespace Samples.Cluster.Metrics
{
    class Program
    {
        private const int UpToN = 10;

        static async Task Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("Application.conf"));

            // create an Akka system
            var system = ActorSystem.Create("ClusterSystem", config);

            // create an actor that handles metric events
            system.ActorOf(Props.Create(typeof(MetricListener)), "metricListener");

            // create the frontend actor
            system.ActorOf(Props.Create(() => new FactorialFrontend(UpToN, true)), "factorialFrontend");

            Console.ReadKey();

            await system.Terminate();
        }
    }
}
