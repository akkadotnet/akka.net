using System.Threading;
using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    public class MinimalActorRefThroughputSpec : ActorThroughputSpecBase
    {
        /// <summary>
        /// Not thread-safe, but called by a single thread in the benchmark
        /// </summary>
        private class BenchmarkActorRef : MinimalActorRef
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkActorRef(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;
            }

            protected override void TellInternal(object message, IActorRef sender)
            {
                _counter.Increment();
                if (++_currentMessages >= _maxExpectedMessages)
                    _resetEvent.Set();
            }

            public override ActorPath Path { get { return null;} }
            public override IActorRefProvider Provider { get {return null;} }
        }

        public override IActorRef CreateBenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
        {
            return new BenchmarkActorRef(counter, maxExpectedMessages, resetEvent);
        }
    }
}