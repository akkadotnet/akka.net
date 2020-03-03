//-----------------------------------------------------------------------
// <copyright file="FlowTimedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Extra;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Extra
{
    public class FlowTimedSpec : ScriptedTest
    {
        private readonly ITestOutputHelper _helper;
        private ActorMaterializer Materializer { get; }

        public FlowTimedSpec(ITestOutputHelper helper) : base(helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Timed_Source_must_measure_time_it_takes_between_elements_matching_a_predicate()
        {
            var testActor = CreateTestProbe();
            const int measureBetweenEvery = 5;

            Action<TimeSpan> printInfo = interval =>
            {
                testActor.Tell(interval);
                _helper.WriteLine($"Measured interval between {measureBetweenEvery} elements was {interval}");
            };

            var n = 20;
            var testRuns = new[] {1, 2};
            Func<Script<int, int>> script =
                () =>
                    Script.Create(
                        Enumerable.Range(1, n)
                            .Select(x => ((ICollection<int>)new[] { x }, (ICollection<int>)new[] { x })).ToArray());
            testRuns.ForEach(
                _ =>
                    RunScript(script(), Materializer.Settings,
                        flow =>flow.Select(x => x)
                                .TimedIntervalBetween(i => i%measureBetweenEvery == 0, printInfo)));

            var expectedNrOfOnIntervalCalls = testRuns.Length*((n/measureBetweenEvery) - 1); // first time has no value to compare to, so skips calling onInterval
            Enumerable.Range(1,expectedNrOfOnIntervalCalls).ForEach(_=> testActor.ExpectMsg<TimeSpan>());
        }

        [Fact]
        public void Timed_Source_must_measure_time_it_takes_from_start_to_complete_by_wrapping_operations()
        {
            var testActor = CreateTestProbe();
            var n = 50;

            Action<TimeSpan> printInfo = d =>
            {
                testActor.Tell(d);
                _helper.WriteLine($"Processing {n} elements took {d}");
            };

            var testRuns = new[] {1, 2, 3};
            Func<Script<int, int>> script =
                () =>
                    Script.Create(
                        Enumerable.Range(1, n)
                            .Select(x => ((ICollection<int>)new[] { x }, (ICollection<int>)new[] { x })).ToArray());

            testRuns.ForEach(
                _ => RunScript(script(), Materializer.Settings, flow => flow.Timed(f => f.Select(x => x), printInfo)));
            testRuns.ForEach(_ => testActor.ExpectMsg<TimeSpan>());
            testActor.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }


        [Fact]
        public void Timed_Flow_must_measure_time_it_takes_between_elements_matching_a_predicate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();

                var flow =
                    Flow.Create<int>().Select(x => (long) x).TimedIntervalBetween(i => i%2 == 1, d => probe.Tell(d));

                var c1 = this.CreateManualSubscriberProbe<long>();
                Source.From(Enumerable.Range(1, 3)).Via(flow).RunWith(Sink.FromSubscriber(c1), Materializer);

                var s = c1.ExpectSubscription();
                s.Request(100);
                c1.ExpectNext(1L);
                c1.ExpectNext(2L);
                c1.ExpectNext(3L);
                c1.ExpectComplete();

                var duration = probe.ExpectMsg<TimeSpan>();
                _helper.WriteLine($"Got duration (first): {duration}");
            }, Materializer);
        }

        [Fact]
        public void Timed_Flow_must_measure_time_it_takes_from_start_to_complete_by_wrapping_operations()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();

                var flow =
                    Flow.Create<int>()
                        .Timed(f => f.Select(x => (double)x).Select(x => (int)x).Select(x => x.ToString()),
                            d => probe.Tell(d))
                        .Select(s => s + "!");

                var t = flow.RunWith(Source.AsSubscriber<int>(), Sink.AsPublisher<string>(false), Materializer);
                var flowIn = t.Item1;
                var flowOut = t.Item2;

                var c1 = this.CreateManualSubscriberProbe<string>();
                flowOut.Subscribe(c1);

                var p = Source.From(Enumerable.Range(0, 101)).RunWith(Sink.AsPublisher<int>(false), Materializer);
                p.Subscribe(flowIn);

                var sub = c1.ExpectSubscription();
                sub.Request(200);
                Enumerable.Range(0, 101).ForEach(i => c1.ExpectNext(i + "!"));
                c1.ExpectComplete();

                var duration = probe.ExpectMsg<TimeSpan>();
                _helper.WriteLine($"Took: {duration}");
            }, Materializer);
        }
    }
}
