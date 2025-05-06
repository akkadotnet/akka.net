//-----------------------------------------------------------------------
// <copyright file="SpawnActorBenchmarks.cs" company="Akka.NET Project">
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
    public class SpawnActorBenchmarks
    {
        [Params(100_000)]
        public int ActorCount { get;set; }
        
        [Params(true, false)]
        public bool EnableTelemetry { get; set; }
        
        private ActorSystem _system;

        [IterationSetup]
        public void Setup()
        {
            if(EnableTelemetry) // need to measure the impact of publishing actor start / stop events
                _system = ActorSystem.Create("system", "akka.actor.telemetry.enabled = true");
            else
                _system = ActorSystem.Create("system");
        }

        [IterationCleanup]
        public void Cleanup()
        {
           _system.Terminate().Wait();
        }

        [Benchmark]
        [BenchmarkCategory(MacroBenchmark, ActorSpawningBenchmark)]
        public async Task Actor_spawn()
        {
            var parent = _system.ActorOf(Parent.Props);
            
            // spawn a bunch of actors
            await parent.Ask<TestDone>(new StartTest(ActorCount), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            
            // terminate the hierarchy
            await parent.GracefulStop(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
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
            public static readonly ChildReady Instance = new();
            private ChildReady() { }
        }

        sealed class TestDone
        {
            public static readonly TestDone Instance = new();
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
