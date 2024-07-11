//-----------------------------------------------------------------------
// <copyright file="ClusterStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Cluster.Tests.Performance.Startup
{
    /// <summary>
    /// Measures end to end startup time, including HOCON parsing
    /// </summary>
    public class ClusterStartup2NodeSpec
    {
        public ActorSystem Sys { get; set; }
        public ActorSystem OtherSys { get; set; }

        public const string SystemConfig = @"akka.actor.provider = cluster
                                             akka.remote.dot-netty.tcp.port = 8110
                                             akka.remote.dot-netty.tcp.hostname = 0.0.0.0
                                             akka.remote.dot-netty.tcp.public-hostname = localhost
                                             akka.cluster.seed-nodes = [""akka.tcp://ClusterSys@localhost:8110""]";

        public TaskCompletionSource<Done> _clusterMemberUp = new();

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            var config = ConfigurationFactory.ParseString(SystemConfig);
            OtherSys = ActorSystem.Create("ClusterSys", config);
            context.Trace.Info($"Started ActorSystem1 on {Cluster.Get(OtherSys).SelfAddress}");
        }

        [PerfBenchmark(Description = "Measures end to end startup time to create a 2 node cluster", NumberOfIterations = 3, RunMode = RunMode.Iterations)]
        [TimingMeasurement]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Run(BenchmarkContext context)
        {
            context.Trace.Info("Configuring ActorSystem2");

            // adding a fallback specifically here as a point of measurement
            var config2 = ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(OtherSys.Settings.Config);
            Sys = ActorSystem.Create("ClusterSys", config2);
            
            Cluster.Get(Sys).RegisterOnMemberUp(() => { _clusterMemberUp.TrySetResult(Done.Instance); });
            context.Trace.Info($"Started ActorSystem1 on {Cluster.Get(OtherSys).SelfAddress}");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _clusterMemberUp.Task.Wait(cts.Token);
            context.Trace.Info("Successfully joined cluster.");
        }

        [PerfCleanup]
        public void Cleanup()
        {
            OtherSys?.Terminate();
            Sys?.Terminate().Wait(TimeSpan.FromSeconds(5));
        }
    }
}
