using System.Threading;
using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    public class ReceiveActorThroughputSpec : ActorThroughputSpecBase
    {
        private class BenchmarkActor : ReceiveActor
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
                ReceiveAny(o =>
                {
                    _counter.Increment();
                    if (++_currentMessages == _maxExpectedMessages)
                        _resetEvent.Set();
                });
            }
        }


        public override IActorRef CreateBenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
        {
            return System.ActorOf(Props.Create(() => new BenchmarkActor(counter, maxExpectedMessages, resetEvent)));
        }
    }
}