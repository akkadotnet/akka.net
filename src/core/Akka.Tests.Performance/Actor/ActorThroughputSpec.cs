//-----------------------------------------------------------------------
// <copyright file="ActorThroughputSpec.cs" company="Akka.NET Project">
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
    ///     Base class used to test the performance of different <see cref="ActorBase" /> implementations
    /// </summary>
    public class ActorThroughputSpec
    {
        #region Actor classes
        internal class BenchmarkActorBasePatternMatchActor : ActorBase
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkActorBasePatternMatchActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;
            }

            protected override bool Receive(object message)
            {
                return message.Match()
                    .With<string>(IncrementAndCheck)
                    .With<int>(IncrementAndCheck)
                    .With<SimpleData>(simpleDataMessage =>
                    {
                        if (simpleDataMessage.Age > 20)
                        {
                            IncrementAndCheck();
                        }
                        else
                        {
                            IncrementAndCheck();
                        }
                    })
                    .Default(o => IncrementAndCheck())
                    .WasHandled;
            }

            private void IncrementAndCheck()
            {
                _counter.Increment();
                if (++_currentMessages == _maxExpectedMessages)
                    _resetEvent.Set();
            }

            public static Props Props(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent) => Akka.Actor.Props.Create(
                () => new BenchmarkActorBasePatternMatchActor(counter, maxExpectedMessages, resetEvent));
        }

        internal class BenchmarkUntypedActor : UntypedActor
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkUntypedActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;
            }

            protected override void OnReceive(object message)
            {
                if (message is string)
                {
                    string stringMessage = (string)message;
                    IncrementAndCheck();
                }
                else if (message is int)
                {
                    int intMessage = (int)message;
                    IncrementAndCheck();
                }
                else if (message is SimpleData)
                {
                    SimpleData simpleDataMessage = (SimpleData)message;
                    if (simpleDataMessage.Age > 20)
                    {
                        IncrementAndCheck();
                    }
                    else
                    {
                        IncrementAndCheck();
                    }
                }
                else
                {
                    IncrementAndCheck();
                }
            }

            private void IncrementAndCheck()
            {
                _counter.Increment();
                if (++_currentMessages == _maxExpectedMessages)
                    _resetEvent.Set();
            }

            public static Props Props(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent) => Akka.Actor.Props.Create(
                () => new BenchmarkUntypedActor(counter, maxExpectedMessages, resetEvent));
        }

        internal class BenchmarkReceiveActor : ReceiveActor
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkReceiveActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;

                Receive<string>(stringMessage => IncrementAndCheck());
                Receive<int>(intMessage => IncrementAndCheck());
                Receive<SimpleData>(simpleDataMessage => simpleDataMessage.Age > 20, simpleDataMessage => IncrementAndCheck());
                Receive<SimpleData>(simpleDataMessage => simpleDataMessage.Age <= 20, simpleDataMessage => IncrementAndCheck());
                ReceiveAny(message => IncrementAndCheck());
            }

            private void IncrementAndCheck()
            {
                _counter.Increment();
                if (++_currentMessages == _maxExpectedMessages)
                    _resetEvent.Set();
            }

            public static Props Props(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent) => Akka.Actor.Props.Create(
                () => new BenchmarkReceiveActor(counter, maxExpectedMessages, resetEvent));
        }

        /// <summary>
        /// Not thread-safe, but called by a single thread in the benchmark
        /// </summary>
        internal class BenchmarkMinimalActorRef : MinimalActorRef
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkMinimalActorRef(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
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

            public override ActorPath Path { get { return null; } }
            public override IActorRefProvider Provider { get { return null; } }
        }
        #endregion

        internal sealed class SimpleData
        {
            public SimpleData(string name, int age)
            {
                Name = name;
                Age = age;
            }

            public string Name { get; }

            public int Age { get; }
        }

        private SimpleData dataExample = new SimpleData("John", 25);
        private int intExample = 343;
        private string stringExample = "just_string";

        private const string MailboxCounterName = "MessageReceived";
        private const long MailboxMessageCount = 10000000;
        private Counter _mailboxThroughput;
        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);

        private IActorRef _actorBasePatternMatchActorRef;
        private IActorRef _untypedActorRef;
        private IActorRef _receiveActorRef;
        private IActorRef _minimalActorRef;

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        protected ActorSystem System;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _mailboxThroughput = context.GetCounter(MailboxCounterName);
            System = ActorSystem.Create($"{GetType().Name}{Counter.GetAndIncrement()}");

            _actorBasePatternMatchActorRef = System.ActorOf(BenchmarkActorBasePatternMatchActor.Props(_mailboxThroughput, MailboxMessageCount * 3, _resetEvent));
            _untypedActorRef = System.ActorOf(BenchmarkUntypedActor.Props(_mailboxThroughput, MailboxMessageCount * 3, _resetEvent));
            _receiveActorRef = System.ActorOf(BenchmarkReceiveActor.Props(_mailboxThroughput, MailboxMessageCount * 3, _resetEvent));
            _minimalActorRef = new BenchmarkMinimalActorRef(_mailboxThroughput, MailboxMessageCount, _resetEvent);
        }

        [PerfBenchmark(
            Description = "Measures the throughput of an ActorBase + Pattern match class",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void ActorBase_PatternMatch_Throughput(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
                _actorBasePatternMatchActorRef.Tell(dataExample);
                _actorBasePatternMatchActorRef.Tell(intExample);
                _actorBasePatternMatchActorRef.Tell(stringExample);
                ++i;
            }
            _resetEvent.Wait(); //wait up to a second
        }

        [PerfBenchmark(
            Description = "Measures the throughput of an UntypedActor",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void UntypedActor_Throughput(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
                _untypedActorRef.Tell(dataExample);
                _untypedActorRef.Tell(intExample);
                _untypedActorRef.Tell(stringExample);
                ++i;
            }
            _resetEvent.Wait(); //wait up to a second
        }

        [PerfBenchmark(
            Description = "Measures the throughput of an ReceiveActor",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void ReceiveActor_Throughput(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
                _receiveActorRef.Tell(dataExample);
                _receiveActorRef.Tell(intExample);
                _receiveActorRef.Tell(stringExample);
                ++i;
            }
            _resetEvent.Wait(); //wait up to a second
        }

        [PerfBenchmark(
            Description = "Measures the throughput of an MinimalActorRef",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void MinimalActorRef_Throughput(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
                _minimalActorRef.Tell(dataExample);
                _minimalActorRef.Tell(intExample);
                _minimalActorRef.Tell(stringExample);
                ++i;
            }
            _resetEvent.Wait(); //wait up to a second
        }

        [PerfCleanup]
        public void Cleanup()
        {
            System.Terminate().Wait(TimeSpan.FromSeconds(2.0d));
            System = null;
            _resetEvent.Dispose();
        }
    }
}
