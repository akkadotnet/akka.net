//-----------------------------------------------------------------------
// <copyright file="ActorSelectionBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))] // need memory diagnosis
    public class ActorSelectionBenchmark
    {
        [Params(10000)]
        public int Iterations { get; set; }
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _echo;

        // cached selection for measuring .Tell / .Ask performance
        private ActorSelection _actorSelection;

        [GlobalSetup]
        public void Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _echo = _system.ActorOf(Props.Create(() => new EchoActor()), "echo");
            _actorSelection = _system.ActorSelection("/user/echo");
        }

        [Benchmark]
        public async Task RequestResponseActorSelection()
        {
            for(var i = 0; i < Iterations; i++)
                await _actorSelection.Ask("foo", _timeout);
        }

        [Benchmark]
        public void CreateActorSelection()
        {
            for (var i = 0; i < Iterations; i++)
                _system.ActorSelection("/user/echo");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
          _system.Terminate().Wait();
        }

        public class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }
    }
}
