//-----------------------------------------------------------------------
// <copyright file="FlowSelectBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using NBench;
using Reactive.Streams;

namespace Akka.Streams.Tests.Performance
{
    // JVM : FlowMapBenchmark
    public class FlowSelectBenchmark
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka {
    log-config-on-start = off
    log-dead-letters-during-shutdown = off
    loglevel = ""WARNING""
    actor.default- dispatcher {
        #executor = ""thread-pool-executor""
        throughput = 1024
    }
    # actor.default-mailbox {
    #    mailbox-type = ""akka.dispatch.SingleConsumerOnlyUnboundedMailbox""
    # }
    test {
        timefactor =  1.0
        filter-leeway = 3s
        single-expect-default = 3s
        default-timeout = 5s
        calling-thread-dispatcher {
        type = akka.testkit.CallingThreadDispatcherConfigurator
        }
    }
}
");

        private ActorSystem _actorSystem;
        private ActorMaterializer _materializer8;
        private ActorMaterializer _materializer32;
        private ActorMaterializer _materializer128;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _actorSystem = ActorSystem.Create("FlowSelectBenchmark", Config.WithFallback(
                ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf")));
            _actorSystem.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());

            var buffer8 = ActorMaterializerSettings.Create(_actorSystem).WithInputBuffer(8, 8);
            _materializer8 = _actorSystem.Materializer(buffer8,"8_");

            var buffer32 = ActorMaterializerSettings.Create(_actorSystem).WithInputBuffer(32, 32);
            _materializer32 = _actorSystem.Materializer(buffer32, "32_");

            var buffer128 = ActorMaterializerSettings.Create(_actorSystem).WithInputBuffer(128, 128);
            _materializer128 = _actorSystem.Materializer(buffer128, "128_");
        }

        [PerfCleanup]
        public void Shutdown() => _actorSystem.Terminate().Wait(TimeSpan.FromSeconds(5));




        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description ="Test the performance of a Select flow with InputBufferSize of 8, 1 Select and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 300)]
        public void Flow_Select_100k_elements_with_buffer_8_and_1_select_and_without_GraphStage_Identifier()
            => Execute(_materializer8, 1, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 1 Select and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 300)]
        public void Flow_Select_100k_elements_with_buffer_32_and_1_select_and_without_GraphStage_Identifier()
            => Execute(_materializer32, 1, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 1 Select and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 300)]
        public void Flow_Select_100k_elements_with_buffer_128_and_1_select_and_without_GraphStage_Identifier()
            => Execute(_materializer128, 1, false);

        

        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 8, 1 Select and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_8_and_1_select_and_with_GraphStage_Identifier()
            => Execute(_materializer8, 1, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 1 Select and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_32_and_1_select_and_with_GraphStage_Identifier()
            => Execute(_materializer32, 1, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 1 Select and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_128_and_1_select_and_with_GraphStage_Identifier()
            => Execute(_materializer128, 1, true);

        




        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 8, 5 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 700)]
        public void Flow_Select_100k_elements_with_buffer_8_and_5_select_and_without_GraphStage_Identifier()
            => Execute(_materializer8, 5, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 5 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 700)]
        public void Flow_Select_100k_elements_with_buffer_32_and_5_select_and_without_GraphStage_Identifier()
            => Execute(_materializer32, 5, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 5 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 700)]
        public void Flow_Select_100k_elements_with_buffer_128_and_5_select_and_without_GraphStage_Identifier()
            => Execute(_materializer128, 5, false);



        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 8, 5 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_8_and_5_select_and_with_GraphStage_Identifier()
            => Execute(_materializer8, 5, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 5 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_32_and_5_select_and_with_GraphStage_Identifier()
            => Execute(_materializer32, 5, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 5 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_128_and_5_select_and_with_GraphStage_Identifier()
            => Execute(_materializer128, 5, true);






        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 8, 10 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1200)]
        public void Flow_Select_100k_elements_with_buffer_8_and_10_select_and_without_GraphStage_Identifier()
            => Execute(_materializer8, 10, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 10 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1200)]
        public void Flow_Select_100k_elements_with_buffer_32_and_10_select_and_without_GraphStage_Identifier()
            => Execute(_materializer32, 10, false);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 10 Selects and without GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1200)]
        public void Flow_Select_100k_elements_with_buffer_128_and_10_select_and_without_GraphStage_Identifier()
            => Execute(_materializer128, 10, false);



        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 8, 10 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_8_and_10_select_and_with_GraphStage_Identifier()
            => Execute(_materializer8, 10, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 32, 10 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_32_and_10_select_and_with_GraphStage_Identifier()
            => Execute(_materializer32, 10, true);


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Description = "Test the performance of a Select flow with InputBufferSize of 128, 10 Selects and with GraphStages.Identity")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Flow_Select_100k_elements_with_buffer_128_and_10_select_and_with_GraphStage_Identifier()
            => Execute(_materializer128, 10, true);





        private void Execute(ActorMaterializer actorMaterializer, int numberOfSelectOps, bool useGraphStageIdentity)
        {
            var syncTestPublisher = new SyncTestPublisher();

            var flow = MakeSelects(Source.FromPublisher(syncTestPublisher), numberOfSelectOps, () =>
            {
                if (useGraphStageIdentity)
                    return GraphStages.Identity<int>();
                return Flow.Create<int>().Select(x => x);
            });

            var l = new AtomicBoolean();

            flow.RunWith(Sink.OnComplete<int>(() => l.Value = true, _ => { }), actorMaterializer);

            while (!l.Value)
                Thread.Sleep(10);
        }

        private sealed class SyncTestPublisher : IPublisher<int>
        {
            private sealed class Subscription : ISubscription
            {
                private readonly ISubscriber<int> _subscriber;
                private int counter; // Piggyback on caller thread, no need for volatile

                public Subscription(ISubscriber<int> subscriber)
                {
                    _subscriber = subscriber;
                }

                public void Request(long n)
                {
                    var i = n;
                    while (i > 0)
                    {
                        _subscriber.OnNext(counter++);
                        if (counter == 100000)
                        {
                            _subscriber.OnComplete();
                            return;
                        }
                        i--;
                    }
                }

                public void Cancel()
                {
                }
            }

            public void Subscribe(ISubscriber<int> subscriber) => subscriber.OnSubscribe(new Subscription(subscriber));
        }

        private static Source<TOut, TMat> MakeSelects<TOut, TMat>(Source<TOut, TMat> source, int count, Func<IGraph<FlowShape<TOut, TOut>, TMat>> flow)
        {
            var f = source;
            for (var i = 0; i < count; i++)
                f = f.Via(flow());
            return f;
        }
    }
}
