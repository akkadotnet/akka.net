//-----------------------------------------------------------------------
// <copyright file="ActorUidBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [MemoryDiagnoser]
    public class ActorUidBenchmarks
    {
        // true: per-system counter UIDs (Artery); false: legacy random UIDs
        [Params(false, true)]
        public bool ArteryEnabled { get; set; }

        private ActorSystem _system;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("uid-bench",
                ConfigurationFactory.ParseString($"akka.remote.artery.enabled = {(ArteryEnabled ? "on" : "off")}"));
        }

        [GlobalCleanup]
        public void Cleanup() => _system.Terminate().Wait();

        [Benchmark]
        public IActorRef ActorOf_trivial_child()
        {
            // children are intentionally not stopped; the cost under test is ActorOf itself
            return _system.ActorOf(Props.Create<Noop>());
        }

        private sealed class Noop : ReceiveActor
        {
            public Noop() => ReceiveAny(_ => { });
        }
    }
}
