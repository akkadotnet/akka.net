//-----------------------------------------------------------------------
// <copyright file="SpawnActorBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Jobs;
using BenchmarkDotNet.Engines;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MonitoringConfig))]
    [SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, targetCount: 1)]
    public class SpawnActorBenchmarks
    {
        public const int ActorCount = 100_000;
        private TimeSpan timeout;
        private ActorSystem system;

        [GlobalSetup]
        public void Setup()
        {
            timeout = TimeSpan.FromMinutes(1);
            system = ActorSystem.Create("system");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = ActorCount)]
        public async Task Actor_spawn()
        {
            var parent = system.ActorOf(Parent.Props);
            await parent.Ask(StartTest.Instance, timeout);
        }

        #region actors

        sealed class StartTest
        {
            public static readonly StartTest Instance = new StartTest();
            private StartTest() { }
        }

        sealed class ChildReady
        {
            public static readonly ChildReady Instance = new ChildReady();
            private ChildReady() { }
        }

        sealed class TestDone
        {
            public static readonly TestDone Instance = new TestDone();
            private TestDone() { }
        }

        sealed class Parent : ReceiveActor
        {
            public static readonly Props Props = Props.Create<Parent>();
            private int count = ActorCount - 1; // -1 because we also create the parent
            private IActorRef replyTo;
            public Parent()
            {
                Receive<StartTest>(_ =>
                {
                    replyTo = Sender;
                    for (int i = 0; i < count; i++)
                    {
                        Context.ActorOf(Child.Props);
                    }
                });
                Receive<ChildReady>(_ =>
                {
                    count--;
                    if (count == 0)
                    {
                        replyTo.Tell(TestDone.Instance);
                    }
                });
            }
        }

        sealed class Child : ReceiveActor
        {
            public static readonly Props Props = Props.Create<Child>();
            public Child()
            {
                ReceiveAny(_ => {});
            }

            protected override void PreStart()
            {
                base.PreStart();
                Context.Parent.Tell(ChildReady.Instance);
            }
        }

        #endregion
    }
}
