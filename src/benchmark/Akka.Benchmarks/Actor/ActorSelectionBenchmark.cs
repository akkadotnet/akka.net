using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))] // need memory diagnosis
    public class ActorSelectionBenchmark
    {
        public const int Operations = 1_000_000;
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
            await _actorSelection.Ask("foo", _timeout);
        }

        [Benchmark]
        public void CreateActorSelection()
        {
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
