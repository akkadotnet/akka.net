//-----------------------------------------------------------------------
// <copyright file="ReceiveAsyncBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Actor
{
    /// <summary>
    /// Measures the per-message cost of running an async handler registered with ReceiveAsync,
    /// against the same actor using a synchronous Receive handler.
    /// </summary>
    [Config(typeof(DefaultRuntimeMacroConfig))]
    [MemoryDiagnoser]
    public class ReceiveAsyncBenchmarks
    {
        /// <summary>
        /// The <see cref="MacroBenchmarkConfig"/> job, minus its GC overrides, so the numbers reflect the runtime's
        /// default settings that most actor systems run with.
        /// </summary>
        private sealed class DefaultRuntimeMacroConfig : ManualConfig
        {
            public DefaultRuntimeMacroConfig()
            {
                AddExporter(MarkdownExporter.GitHub);
                AddColumn(new RequestsPerSecondColumn());
                AddColumn(new CategoriesColumn());
                AddLogger(ConsoleLogger.Default);
                AddJob(Job.LongRun
                    .WithWarmupCount(3)
                    .WithIterationCount(10)
                    .RunOncePerIteration()
                    .WithStrategy(RunStrategy.Monitoring));
            }
        }

        private const int Operations = 100_000;
        private const int RoundTrips = 50_000;
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _sync;
        private IActorRef _completed;
        private IActorRef _yielding;
        private IActorRef _incomplete;
        private IActorRef _syncPinger;
        private IActorRef _asyncPinger;

        [IterationSetup]
        public void Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _sync = _system.ActorOf(Props.Create(() => new SyncActor()));
            _completed = _system.ActorOf(Props.Create(() => new CompletedActor()));
            _yielding = _system.ActorOf(Props.Create(() => new YieldingActor()));
            _incomplete = _system.ActorOf(Props.Create(() => new IncompleteActor()));
            var syncPonger = _system.ActorOf(Props.Create(() => new SyncPonger()));
            _syncPinger = _system.ActorOf(Props.Create(() => new SyncPinger(syncPonger)));
            var asyncPonger = _system.ActorOf(Props.Create(() => new AsyncPonger()));
            _asyncPinger = _system.ActorOf(Props.Create(() => new AsyncPinger(asyncPonger)));
        }

        [IterationCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task Receive_sync()
        {
            await Run(_sync);
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task ReceiveAsync_completed_task()
        {
            await Run(_completed);
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task ReceiveAsync_task_yield()
        {
            await Run(_yielding);
        }

        /// <summary>
        /// A handler whose task is still running when the handler returns: it awaits work on the thread pool,
        /// so the rest of the handler resumes through the actor's task scheduler.
        /// </summary>
        [Benchmark(OperationsPerInvoke = Operations)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task ReceiveAsync_incomplete_task()
        {
            await Run(_incomplete);
        }

        /// <summary>
        /// Two actors exchanging Ping/Pong with synchronous Receive handlers; one operation is one round trip.
        /// </summary>
        [Benchmark(OperationsPerInvoke = RoundTrips)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task PingPong_Receive_sync()
        {
            await _syncPinger.Ask<Done>(new Start(RoundTrips), _timeout);
        }

        /// <summary>
        /// Two actors exchanging Ping/Pong with ReceiveAsync handlers that return an already completed task;
        /// one operation is one round trip.
        /// </summary>
        [Benchmark(OperationsPerInvoke = RoundTrips)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task PingPong_ReceiveAsync_completed_task()
        {
            await _asyncPinger.Ask<Done>(new Start(RoundTrips), _timeout);
        }

        private async Task Run(IActorRef actor)
        {
            for (var i = 0; i < Operations; i++)
                actor.Tell(Work.Instance);
            await actor.Ask(Done.Instance, _timeout);
        }

        sealed class Work
        {
            public static readonly Work Instance = new();
            private Work() { }
        }

        sealed class Done
        {
            public static readonly Done Instance = new();
            private Done() { }
        }

        sealed class Start
        {
            public Start(int roundTrips) => RoundTrips = roundTrips;
            public int RoundTrips { get; }
        }

        sealed class Ping
        {
            public static readonly Ping Instance = new();
            private Ping() { }
        }

        sealed class Pong
        {
            public static readonly Pong Instance = new();
            private Pong() { }
        }

        sealed class SyncActor : ReceiveActor
        {
            public SyncActor()
            {
                Receive<Work>(_ => { });
                Receive<Done>(_ => Sender.Tell(Done.Instance));
            }
        }

        sealed class CompletedActor : ReceiveActor
        {
            public CompletedActor()
            {
                ReceiveAsync<Work>(_ => Task.CompletedTask);
                Receive<Done>(_ => Sender.Tell(Done.Instance));
            }
        }

        sealed class YieldingActor : ReceiveActor
        {
            public YieldingActor()
            {
                ReceiveAsync<Work>(async _ => await Task.Yield());
                Receive<Done>(_ => Sender.Tell(Done.Instance));
            }
        }

        sealed class IncompleteActor : ReceiveActor
        {
            public IncompleteActor()
            {
                ReceiveAsync<Work>(async _ => await Task.Run(static () => { }));
                Receive<Done>(_ => Sender.Tell(Done.Instance));
            }
        }

        sealed class SyncPinger : ReceiveActor
        {
            private IActorRef _replyTo = ActorRefs.Nobody;
            private int _remaining;

            public SyncPinger(IActorRef ponger)
            {
                Receive<Start>(start =>
                {
                    _replyTo = Sender;
                    _remaining = start.RoundTrips;
                    ponger.Tell(Ping.Instance);
                });
                Receive<Pong>(_ =>
                {
                    if (--_remaining == 0)
                        _replyTo.Tell(Done.Instance);
                    else
                        Sender.Tell(Ping.Instance);
                });
            }
        }

        sealed class SyncPonger : ReceiveActor
        {
            public SyncPonger()
            {
                Receive<Ping>(_ => Sender.Tell(Pong.Instance));
            }
        }

        sealed class AsyncPinger : ReceiveActor
        {
            private IActorRef _replyTo = ActorRefs.Nobody;
            private int _remaining;

            public AsyncPinger(IActorRef ponger)
            {
                Receive<Start>(start =>
                {
                    _replyTo = Sender;
                    _remaining = start.RoundTrips;
                    ponger.Tell(Ping.Instance);
                });
                ReceiveAsync<Pong>(_ =>
                {
                    if (--_remaining == 0)
                        _replyTo.Tell(Done.Instance);
                    else
                        Sender.Tell(Ping.Instance);
                    return Task.CompletedTask;
                });
            }
        }

        sealed class AsyncPonger : ReceiveActor
        {
            public AsyncPonger()
            {
                ReceiveAsync<Ping>(_ =>
                {
                    Sender.Tell(Pong.Instance);
                    return Task.CompletedTask;
                });
            }
        }
    }
}
