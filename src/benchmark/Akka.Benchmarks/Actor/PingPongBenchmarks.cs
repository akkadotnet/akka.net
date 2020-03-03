//-----------------------------------------------------------------------
// <copyright file="PingPongBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MonitoringConfig))]
    [SimpleJob(RunStrategy.Monitoring, launchCount: 3, warmupCount: 3, targetCount: 3)]
    public class PingPongBenchmarks
    {
        public const int Operations = 1_000_000;
        private TimeSpan timeout;
        private ActorSystem system;
        private IActorRef ping;

        [GlobalSetup]
        public void Setup()
        {
            timeout = TimeSpan.FromMinutes(1);
            system = ActorSystem.Create("system");
            var pong = system.ActorOf(Props.Create(() => new Pong()));
            ping = system.ActorOf(Props.Create(() => new Ping(pong)));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public async Task Actor_ping_pong_single_pair_in_memory()
        {
            await ping.Ask(StartTest.Instance, timeout);
        }

        #region actors

        sealed class StartTest
        {
            public static readonly StartTest Instance = new StartTest();
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
            public static readonly TestDone Instance = new TestDone();
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
