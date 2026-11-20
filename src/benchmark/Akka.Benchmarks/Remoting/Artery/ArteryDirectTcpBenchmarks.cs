// -----------------------------------------------------------------------
// <copyright file="ArteryDirectTcpBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Artery;
using Akka.Streams;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Loopback remote byte-array echoes with identical Artery settings except the outbound TCP adapter.
    /// Each worker verifies adapter engagement and owner retirement. Results include actors, serialization,
    /// both network directions and Ask completion; they do not isolate socket or stage execution time.
    /// </summary>
    [MemoryDiagnoser]
    [Config(typeof(DirectTcpConfig))]
    public class ArteryDirectTcpBenchmarks
    {
        private const int Exchanges = 4096;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private ActorSystem? _sender;
        private ActorSystem? _receiver;
        private IActorRef _echo = null!;
        private byte[] _payload = null!;

        [Params(false, true)]
        public bool DirectOutbound { get; set; }

        [Params(1, 32)]
        public int Parallelism { get; set; }

        [Params(32, 1024)]
        public int PayloadBytes { get; set; }

        [GlobalSetup]
        public async Task Setup()
        {
            var config = ConfigurationFactory.ParseString($"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.outbound-lanes = 4
                akka.remote.artery.advanced.inbound-lanes = 2
                akka.remote.artery.advanced.tcp.direct-outbound = {(DirectOutbound ? "on" : "off")}
                akka.loglevel = WARNING
                akka.log-dead-letters = off
                akka.log-dead-letters-during-shutdown = off
                """);
            _payload = Enumerable.Range(0, PayloadBytes).Select(index => (byte)index).ToArray();
            try
            {
                _sender = ActorSystem.Create("echoSender", config);
                _receiver = ActorSystem.Create("echoReceiver", config);
                _receiver.ActorOf(Props.Create(() => new Echo()), "echo");
                var address = ((ExtendedActorSystem)_receiver).Provider.DefaultAddress;
                _echo = await _sender.ActorSelection($"{address}/user/echo").ResolveOne(Timeout);
                await RunLane(32);
                var counts = new[] { NativeConnections(_sender), NativeConnections(_receiver) };
                if (counts.Any(count => (count > 0) != DirectOutbound))
                    throw new InvalidOperationException("Configured adapter did not match active native owner counts.");

                // Warm the same workload for a fixed duration before the adaptive pilot and warmup iterations.
                var warmStarted = Stopwatch.GetTimestamp();
                var warmedExchanges = 0L;
                do
                {
                    warmedExchanges += await EchoBatch();
                } while (Stopwatch.GetElapsedTime(warmStarted) < TimeSpan.FromSeconds(10));
                var warmElapsed = Stopwatch.GetElapsedTime(warmStarted);
                counts = new[] { NativeConnections(_sender), NativeConnections(_receiver) };
                if (counts.Any(count => (count > 0) != DirectOutbound))
                    throw new InvalidOperationException("Adapter engagement changed during workload warmup.");

                // Retain worker-local identity, rather than assuming the host's output was loaded by generated workers.
                Console.WriteLine("DIRECT_TCP_SETUP " + JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId,
                    direct_outbound = DirectOutbound,
                    parallelism = Parallelism,
                    payload_bytes = PayloadBytes,
                    native_owners = counts,
                    workload_warmup_target_seconds = 10,
                    workload_warmup_elapsed_seconds = warmElapsed.TotalSeconds,
                    workload_warmup_exchanges = warmedExchanges,
                    framework = RuntimeInformation.FrameworkDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    runtime_version = Environment.Version.ToString(),
                    command_line = Environment.GetCommandLineArgs(),
                    working_directory = Environment.CurrentDirectory,
                    assemblies = new[] { typeof(ActorSystem).Assembly, typeof(ArteryRemoting).Assembly, typeof(ActorMaterializer).Assembly }
                        .Select(assembly => new
                        {
                            name = assembly.GetName().Name,
                            path = assembly.Location,
                            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
                            informational_version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        }).ToArray()
                }));
            }
            catch
            {
                await Cleanup();
                throw;
            }
        }

        [Benchmark(OperationsPerInvoke = Exchanges)]
        public async Task<int> EchoBatch()
        {
            var tasks = new Task<int>[Parallelism];
            for (var lane = 0; lane < tasks.Length; lane++)
                tasks[lane] = RunLane(Exchanges / Parallelism);
            var completed = (await Task.WhenAll(tasks)).Sum();
            if (completed != Exchanges)
                throw new InvalidOperationException($"Expected {Exchanges} echoes, got {completed}.");
            return completed;
        }

        private async Task<int> RunLane(int exchanges)
        {
            for (var index = 0; index < exchanges; index++)
            {
                var echoed = await _echo.Ask<byte[]>(_payload, Timeout);
                if (!echoed.AsSpan().SequenceEqual(_payload))
                    throw new InvalidOperationException("Echo payload changed.");
            }
            return exchanges;
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            var systems = new[] { _sender, _receiver }.Where(system => system is not null).Cast<ActorSystem>().ToArray();
            await Task.WhenAll(systems.Select(system => system.Terminate())).WaitAsync(TimeSpan.FromSeconds(30));
            var counts = systems.Select(NativeConnections).ToArray();
            if (counts.Any(count => count != 0))
                throw new InvalidOperationException("Native owners remained after system termination.");
            Console.WriteLine("DIRECT_TCP_CLEANUP " + JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId,
                direct_outbound = DirectOutbound,
                systems = systems.Length,
                native_owners = counts
            }));
        }

        private static int NativeConnections(ActorSystem system) =>
            ((ArteryRemoting)((RemoteActorRefProvider)((ExtendedActorSystem)system).Provider).Transport).DirectTcpConnectionCount;

        private sealed class Echo : ReceiveActor
        {
            public Echo() => Receive<byte[]>(message => Sender.Tell(message));
        }

        public sealed class DirectTcpConfig : ManualConfig
        {
            public DirectTcpConfig()
            {
                var job = Job.Default.WithRuntime(CoreRuntime.Core10_0)
                    .WithLaunchCount(1).WithWarmupCount(8).WithIterationCount(8)
                    .WithIterationTime(TimeInterval.FromMilliseconds(250)).WithUnrollFactor(1)
                    .WithGcServer(true).WithGcConcurrent(true).WithId("visitA");
                AddJob(job);
                AddJob(job.WithId("visitB"));
                WithOrderer(new EchoOrderer());
                WithOptions(ConfigOptions.KeepBenchmarkFiles);
            }
        }

        private sealed class EchoOrderer : IOrderer
        {
            public bool SeparateLogicalGroups => true;

            public IEnumerable<BenchmarkCase> GetExecutionOrder(ImmutableArray<BenchmarkCase> benchmarksCase,
                IEnumerable<BenchmarkLogicalGroupRule>? order = null) => Arrange(benchmarksCase);

            public IEnumerable<BenchmarkCase> GetSummaryOrder(ImmutableArray<BenchmarkCase> benchmarksCases,
                Summary summary) => Arrange(benchmarksCases);

            public string GetHighlightGroupKey(BenchmarkCase benchmarkCase) => Shape(benchmarkCase);

            public string GetLogicalGroupKey(ImmutableArray<BenchmarkCase> allBenchmarksCases,
                BenchmarkCase benchmarkCase) => Shape(benchmarkCase);

            public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
                IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
                IEnumerable<BenchmarkLogicalGroupRule>? order = null) => logicalGroups.OrderBy(group => group.Key);

            private static string Shape(BenchmarkCase benchmarkCase) =>
                $"bytes={benchmarkCase.Parameters[nameof(PayloadBytes)]},p={benchmarkCase.Parameters[nameof(Parallelism)]}";

            private static int Rank(BenchmarkCase benchmarkCase) =>
                (benchmarkCase.Job.Id, (bool)benchmarkCase.Parameters[nameof(DirectOutbound)]) switch
                {
                    ("visitA", false) => 0,
                    ("visitA", true) => 1,
                    ("visitB", true) => 2,
                    ("visitB", false) => 3,
                    _ => throw new InvalidOperationException("Unexpected job or adapter setting in echo schedule.")
                };

            private static IEnumerable<BenchmarkCase> Arrange(IEnumerable<BenchmarkCase> cases) =>
                cases.OrderBy(benchmarkCase => (int)benchmarkCase.Parameters[nameof(PayloadBytes)])
                    .ThenBy(benchmarkCase => (int)benchmarkCase.Parameters[nameof(Parallelism)])
                    .ThenBy(Rank);
        }
    }
}
