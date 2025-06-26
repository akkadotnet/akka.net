//-----------------------------------------------------------------------
// <copyright file="PingPongBenchmarks.cs" company="Akka.NET Project">
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
    [Config(typeof(MacroBenchmarkConfig))]
    public class PingPongBenchmarks
    {
        private const int Operations = 1_000_000;
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _ping;

        [IterationSetup]
        public void Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            var pong = _system.ActorOf(Props.Create(() => new Pong()));
            _ping = _system.ActorOf(Props.Create(() => new Ping(pong)));
        }

        [IterationCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = Operations * 2)]
        [BenchmarkCategory(MacroBenchmark, ActorMessagingBenchmark)]
        public async Task Actor_ping_pong_single_pair_in_memory()
        {
            await _ping.Ask(StartTest.Instance, _timeout);
        }

        #region actors

        sealed class StartTest
        {
            public static readonly StartTest Instance = new();
            private StartTest() { }
        }

        sealed class Signal
        {
            public int Remaining { get; }

            public Signal(int remaining)
            {
                Remaining = remaining;
            }
        }

        sealed class TestDone
        {
            public static readonly TestDone Instance = new();
            private TestDone() { }
        }

        sealed class Ping : ReceiveActor
        {
            private IActorRef replyTo;

            public Ping(IActorRef pong)
            {
                Receive<StartTest>(_ =>
                {
                    replyTo = Sender;

                    var signal = new Signal(Operations);
                    pong.Tell(signal);
                });

                Receive<Signal>(signal =>
                {
                    var remaining = signal.Remaining;
                    if (remaining <= 0)
                    {
                        replyTo.Tell(TestDone.Instance);
                    }
                    else
                    {
                        Sender.Tell(new Signal(remaining - 1));
                    }
                });
            }
        }
        sealed class Pong : ReceiveActor
        {
            public Pong()
            {
                Receive<Signal>(signal =>
                {
                    Sender.Tell(new Signal(signal.Remaining - 1));
                });
            }
        }

        #endregion

    }
}
