//-----------------------------------------------------------------------
// <copyright file="UntypedActorThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    public class UntypedActorThroughputSpec : ActorThroughputSpecBase
    {
        private class BenchmarkActor : UntypedActor
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;
            }

            protected override void OnReceive(object message)
            {
                _counter.Increment();
                if (++_currentMessages == _maxExpectedMessages)
                    _resetEvent.Set();
            }
        }

        public override IActorRef CreateBenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
        {
            return System.ActorOf(Props.Create(() => new BenchmarkActor(counter, maxExpectedMessages, resetEvent)));
        }
    }
}