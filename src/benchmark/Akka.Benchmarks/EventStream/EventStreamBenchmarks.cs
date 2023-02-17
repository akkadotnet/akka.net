//-----------------------------------------------------------------------
// <copyright file="EventStreamBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.EventStream
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class EventStreamBenchmarks
    {
                
        public const int NumOperations = 1_000_000;
        
        internal sealed class FakeActor : MinimalActorRef
        {
            public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "fake";
            public override IActorRefProvider Provider => throw new System.NotImplementedException();
            
            public int LastMessage { get; private set; }

            protected override void TellInternal(object message, IActorRef sender)
            {
                LastMessage++;
            }
        }
        
        private FakeActor _fakeActor;
        private Akka.Event.EventStream _eventStream;

        private const string _msg = "foo";

        
        [IterationSetup]
        public void InitLogger()
        {
            _eventStream = new Event.EventStream(false);
            _fakeActor = new FakeActor();
            _eventStream.Subscribe(_fakeActor, typeof(string));
        }
        
        [Benchmark(OperationsPerInvoke = NumOperations)]
        public object Publish()
        {
            for (var i = 0; i < NumOperations; i++)
            {
                _eventStream.Publish(_msg);
            }
            return _fakeActor.LastMessage;
        }
    }
}