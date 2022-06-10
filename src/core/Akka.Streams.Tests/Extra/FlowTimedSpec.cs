//-----------------------------------------------------------------------
// <copyright file="FlowTimedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Extra;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Extra
{
    public class FlowTimedSpec : ScriptedTest
    {
        private readonly ITestOutputHelper _helper;
        private ActorMaterializer Materializer { get; }

        public FlowTimedSpec(ITestOutputHelper helper) : base("akka.loglevel = DEBUG", helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task Timed_Source_must_measure_time_it_takes_between_elements_matching_a_predicate()
        {
            var testActor = CreateTestProbe();
            const int measureBetweenEvery = 5;

            void PrintInfo(TimeSpan interval)
            {
                testActor.Tell(interval);
                _helper.WriteLine($"Measured interval between {measureBetweenEvery} elements was {interval}");
            }

            const int n = 20;
            var testRuns = new[] {1, 2};

            Script<int, int> Script() =>
                ScriptedTest.Script.Create(Enumerable.Range(1, n)
                    .Select(x => ((ICollection<int>)new[] { x }, (ICollection<int>)new[] { x }))
                    .ToArray());

            foreach (var _ in testRuns)
            {
                await RunScriptAsync(Script(), Materializer.Settings,
                    flow =>flow.Select(x => x)
                        .TimedIntervalBetween(i => i % measureBetweenEvery == 0, PrintInfo),
                    assertStagesStopped: false);
            }

            var expectedNrOfOnIntervalCalls = testRuns.Length*((n/measureBetweenEvery) - 1); // first time has no value to compare to, so skips calling onInterval
            foreach (var _ in Enumerable.Range(1,expectedNrOfOnIntervalCalls))
            {
                await testActor.ExpectMsgAsync<TimeSpan>();
            }
        }

        [Fact]
        public async Task Timed_Source_must_measure_time_it_takes_from_start_to_complete_by_wrapping_operations()
        {
            var testActor = CreateTestProbe();
            const int n = 50;

            void PrintInfo(TimeSpan d)
            {
                testActor.Tell(d);
                _helper.WriteLine($"Processing {n} elements took {d}");
            }

            var testRuns = new[] {1, 2, 3};

            Script<int, int> Script() =>
                ScriptedTest.Script.Create(Enumerable.Range(1, n)
                    .Select(x => ((ICollection<int>)new[] { x }, (ICollection<int>)new[] { x }))
                    .ToArray());

            foreach (var _ in testRuns)
            {
                await RunScriptAsync(Script(), Materializer.Settings, flow => flow.Timed(f => f.Select(x => x), PrintInfo));
            }
            
            foreach (var _ in testRuns)
            {
                await testActor.ExpectMsgAsync<TimeSpan>();
            }
            await testActor.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }


        [Fact]
        public async Task Timed_Flow_must_measure_time_it_takes_between_elements_matching_a_predicate()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();

                var flow =
                    Flow.Create<int>().Select(x => (long) x).TimedIntervalBetween(i => i % 2 == 1, d => probe.Tell(d));

                var c1 = this.CreateManualSubscriberProbe<long>();
                Source.From(Enumerable.Range(1, 3)).Via(flow).RunWith(Sink.FromSubscriber(c1), Materializer);

                var s = await c1.ExpectSubscriptionAsync();
                s.Request(100);
                await c1.ExpectNextAsync(1L);
                await c1.ExpectNextAsync(2L);
                await c1.ExpectNextAsync(3L);
                await c1.ExpectCompleteAsync();

                var duration = await probe.ExpectMsgAsync<TimeSpan>();
                _helper.WriteLine($"Got duration (first): {duration}");
            }, Materializer);
        }

        [Fact]
        public async Task Timed_Flow_must_measure_time_it_takes_from_start_to_complete_by_wrapping_operations()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();

                var flow =
                    Flow.Create<int>()
                        .Timed(f => f.Select(x => (double)x).Select(x => (int)x).Select(x => x.ToString()),
                            d => probe.Tell(d))
                        .Select(s => s + "!");

                var (flowIn, flowOut) = flow.RunWith(Source.AsSubscriber<int>(), Sink.AsPublisher<string>(false), Materializer);

                var c1 = this.CreateManualSubscriberProbe<string>();
                flowOut.Subscribe(c1);

                var p = Source.From(Enumerable.Range(0, 101)).RunWith(Sink.AsPublisher<int>(false), Materializer);
                p.Subscribe(flowIn);

                var sub = await c1.ExpectSubscriptionAsync();
                sub.Request(200);
                foreach (var i in Enumerable.Range(0, 101))
                {
                    await c1.ExpectNextAsync(i + "!");
                }
                await c1.ExpectCompleteAsync();

                var duration = await probe.ExpectMsgAsync<TimeSpan>();
                _helper.WriteLine($"Took: {duration}");
            }, Materializer);
        }
    }
}
