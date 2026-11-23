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
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Actor
{
    /// <summary>
    /// Measures the per-message cost of running an async handler registered with ReceiveAsync.
    /// </summary>
    [Config(typeof(MacroBenchmarkConfig))]
    [MemoryDiagnoser]
    public class ReceiveAsyncBenchmarks
    {
        private const int Operations = 100_000;
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _completed;
        private IActorRef _yielding;

        [IterationSetup]
        public void Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _completed = _system.ActorOf(Props.Create(() => new CompletedActor()));
            _yielding = _system.ActorOf(Props.Create(() => new YieldingActor()));
        }

        [IterationCleanup]
        public void Cleanup()
        {
            _system.Dispose();
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
    }
}
