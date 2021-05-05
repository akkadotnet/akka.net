//-----------------------------------------------------------------------
// <copyright file="SpawnActorBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    [SimpleJob(RunStrategy.Throughput, targetCount:10, warmupCount:5)]
    public class SpawnActorBenchmarks
    {
        [Params(100_000)]
        public int ActorCount { get;set; }
        private ActorSystem system;

        [IterationSetup]
        public void Setup()
        {
            system = ActorSystem.Create("system");
        }

        [IterationCleanup]
        public void Cleanup()
        {
           system.Terminate().Wait();
        }

        [Benchmark]
        public async Task Actor_spawn()
        {
            var parent = system.ActorOf(Parent.Props);
            await parent.Ask<TestDone>(new StartTest(ActorCount), TimeSpan.FromMinutes(2));
        }

        #region actors

        sealed class StartTest
        {
            public StartTest(int actorCount) {
                ActorCount = actorCount;
            }

            public int ActorCount { get; }
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
            private int count;
            private IActorRef replyTo;
            public Parent()
            {
                Receive<StartTest>(_ =>
                {
                    count = _.ActorCount - 1; // -1 because we also create the parent
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
