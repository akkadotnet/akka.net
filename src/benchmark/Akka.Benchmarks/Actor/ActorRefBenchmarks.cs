// //-----------------------------------------------------------------------
// // <copyright file="ActorRefBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))] // need memory diagnosis
    public class ActorRefBenchmarks
    {
        [Params(10000)]
        public int Iterations { get; set; }
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _echo;
        private IActorRef _echo2;
        
        [GlobalSetup]
        public void Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _echo = _system.ActorOf(Props.Create(() => new EchoActor()), "echo");
            _echo2 = _system.ActorOf(Props.Create(() => new EchoActor()), "echo2");
        }

        [Benchmark]
        public int ActorRefGetHashCode()
        {
            return _echo.GetHashCode();
        }

        [Benchmark]
        public bool ActorRefEqualsSelf()
        {
            return _echo.Equals(_echo);
        }
        
        [Benchmark]
        public bool ActorRefEqualsSomeoneElse()
        {
            return _echo.Equals(_echo2);
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