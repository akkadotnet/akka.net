//-----------------------------------------------------------------------
// <copyright file="FsmBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class FsmBenchmarks
    {
        #region Classes
        
        public enum States
        {
            Initial,
            Running
        }

        public sealed class FsmData
        {
            public FsmData(string d)
            {
                D = d;
            }

            public string D { get; }
        }

        public class BenchmarkFsmActor : FSM<States, FsmData>
        {
            public BenchmarkFsmActor(string init)
            {
                StartWith(States.Initial, new FsmData(init));
                
                When(States.Initial, @e =>
                {
                    switch (e.FsmEvent)
                    {
                        case string str1 when e.StateData.D.Equals("transition"):
                            Sender.Tell(str1);
                            return GoTo(States.Running);
                        case string str2:
                            Sender.Tell(str2);
                            return Stay().Using(new FsmData(str2));
                        default:
                            Sender.Tell(e.FsmEvent);
                            return Stay();
                    }
                });
                
                When(States.Running, @e =>
                {
                    switch (e.FsmEvent)
                    {
                        case string str1 when e.StateData.D.Equals("transition"):
                            Sender.Tell(str1);
                            return GoTo(States.Initial);
                        case string str2:
                            Sender.Tell(str2);
                            return Stay().Using(new FsmData(str2));
                        default:
                            Sender.Tell(e.FsmEvent);
                            return Stay();
                    }
                });
            }
        }

        public class UntypedActorBaseline : UntypedActor
        {
            private FsmData _data;

            public UntypedActorBaseline(string d)
            {
                _data = new FsmData(d);
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case string str1 when _data.D.Equals("transition"):
                        Sender.Tell(str1);
                        break;
                    case string str2:
                        Sender.Tell(str2);
                        _data = new FsmData(str2);
                        break;
                    default:
                        Sender.Tell(message);
                        break;
                }
            }
        }
        
        #endregion
        
        private ActorSystem _sys;
        private IActorRef _fsmActor;
        private IActorRef _untypedActor;
        
        [Params(1_000_000)]
        public int MsgCount { get; set; }
        
        [GlobalSetup]
        public void Setup()
        {
            _sys = ActorSystem.Create("Bench", @"akka.log-dead-letters = off");
            _fsmActor = _sys.ActorOf(Props.Create(() => new BenchmarkFsmActor("start")));
            _untypedActor = _sys.ActorOf(Props.Create(() => new UntypedActorBaseline("start")));
        }

        [GlobalCleanup]
        public async Task CleanUp()
        {
            await _sys.Terminate();
        }

        [Benchmark]
        public async Task BenchmarkFsm()
        {
            for (var i = 0; i < MsgCount; i++)
            {
                if (i % 4 == 0)
                {
                    _fsmActor.Tell("transition");
                }
                else
                {
                    _fsmActor.Tell(i);
                }
            }

            await _fsmActor.Ask<string>("stop");
        }
        
        [Benchmark(Baseline = true)]
        public async Task BenchmarkUntyped()
        {
            for (var i = 0; i < MsgCount; i++)
            {
                if (i % 4 == 0)
                {
                    _untypedActor.Tell("transition");
                }
                else
                {
                    _untypedActor.Tell(i);
                }
            }

            await _untypedActor.Ask<string>("stop");
        }
    }
}
