//-----------------------------------------------------------------------
// <copyright file="SpawnActorBenchmarks.cs" company="Akka.NET Project">
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
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(RunStrategy.Throughput, targetCount:10, warmupCount:5, invocationCount: ActorCount)]
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

        [Benchmark]
        public void Actor_spawn()
        {
            var parent = system.ActorOf(Parent.Props);
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
