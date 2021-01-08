//-----------------------------------------------------------------------
// <copyright file="ActorSelectionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    /// Specs for benchmarking <see cref="ActorSelection"/> instances
    /// </summary>
    public class ActorSelectionSpecs
    {
        private const string ActorSelectionCounterName = "ActorSelectionOperationCompleted";
        private const long NumberOfMessages = 10000L;

        private Counter _selectionOpCounter;
        private IActorRef _receiver;
        private ActorPath _receiverActorPath;

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        protected ActorSystem System;

        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
        private Props _oneMessageBenchmarkProps;

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

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _selectionOpCounter = context.GetCounter(ActorSelectionCounterName);
            System = ActorSystem.Create("MailboxThroughputSpecBase" + Counter.GetAndIncrement());
            _receiver = System.ActorOf(Props.Create(() => new BenchmarkActor(_selectionOpCounter, NumberOfMessages, _resetEvent)));
            _receiverActorPath = _receiver.Path;
            _oneMessageBenchmarkProps = Props.Create(() => new BenchmarkActor(_selectionOpCounter, 1, _resetEvent));
        }

        [PerfBenchmark(Description = "Tests the message delivery throughput of NEW ActorSelections to NEW actors", 
            NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000, TestMode = TestMode.Measurement)]
        [CounterMeasurement(ActorSelectionCounterName)]
        public void New_ActorSelection_on_new_actor_throughput(BenchmarkContext context)
        {
            var actorRef = System.ActorOf(_oneMessageBenchmarkProps); // create a new actor every time
            System.ActorSelection(actorRef.Path).Tell("foo"); // send that actor a message via selection
            _resetEvent.Wait();
            _resetEvent.Reset();
        }

        [PerfBenchmark(Description = "Tests the message delivery throughput of REUSABLE ActorSelections to PRE-EXISTING actors",
            NumberOfIterations = 13, RunMode = RunMode.Iterations, TestMode = TestMode.Measurement)]
        [CounterMeasurement(ActorSelectionCounterName)]
        public void Reused_ActorSelection_on_pre_existing_actor_throughput(BenchmarkContext context)
        {
            var actorSelection = System.ActorSelection(_receiverActorPath);
            for (var i = 0; i < NumberOfMessages;)
            {
                actorSelection.Tell("foo");
                ++i;
            }
            _resetEvent.Wait();
        }

        [PerfBenchmark(Description = "Tests the message delivery throughput of NEW ActorSelections to PRE-EXISTING actors. This is really a stress test.",
            NumberOfIterations = 13, RunMode = RunMode.Iterations, TestMode = TestMode.Measurement)]
        [CounterMeasurement(ActorSelectionCounterName)]
        public void New_ActorSelection_on_pre_existing_actor_throughput(BenchmarkContext context)
        {
            for (var i = 0; i < NumberOfMessages;)
            {
                System.ActorSelection(_receiverActorPath).Tell("foo");
                ++i;
            }
            _resetEvent.Wait();
        }

        [PerfBenchmark(Description = "Tests the throughput of resolving an ActorSelection on a pre-existing actor via ResolveOne",
            NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000, TestMode = TestMode.Measurement)]
        [CounterMeasurement(ActorSelectionCounterName)]
        public void ActorSelection_ResolveOne_throughput(BenchmarkContext context)
        {
            var actorRef= System.ActorSelection(_receiverActorPath).ResolveOne(TimeSpan.FromSeconds(2)).Result; // send that actor a message via selection
            _selectionOpCounter.Increment();
        }

        [PerfBenchmark(Description = "Continuously creates actors and attempts to resolve them immediately. Used to surface race conditions.",
            NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000, TestMode = TestMode.Measurement)]
        [CounterMeasurement(ActorSelectionCounterName)]
        public void ActorSelection_ResolveOne_stress_test(BenchmarkContext context)
        {
            var actorRef = System.ActorOf(_oneMessageBenchmarkProps); // create a new actor every time
            var actorRef2 = System.ActorSelection(actorRef.Path).ResolveOne(TimeSpan.FromSeconds(2)).Result; // send that actor a message via selection
            _selectionOpCounter.Increment();
        }

        [PerfCleanup]
        public void Cleanup()
        {
            System.Terminate().Wait();
            _resetEvent.Dispose();
        }
    }
}
