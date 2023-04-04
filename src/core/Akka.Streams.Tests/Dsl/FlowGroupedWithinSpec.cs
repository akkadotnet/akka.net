//-----------------------------------------------------------------------
// <copyright file="FlowGroupedWithinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupedWithinSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowGroupedWithinSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public void A_GroupedWithin_must_group_elements_within_the_duration()
        {
            this.AssertAllStagesStopped(async() =>
            {
                var input = new Iterator<int>(Enumerable.Range(1, 10000));
                var p = this.CreateManualPublisherProbe<int>();
                var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

                Source.FromPublisher(p)
                    .GroupedWithin(1000, TimeSpan.FromSeconds(1))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);

                var pSub = await p.ExpectSubscriptionAsync();
                var cSub = await c.ExpectSubscriptionAsync();

                cSub.Request(100);

                var demand1 = (int)await pSub.ExpectRequestAsync();
                for (var i = 1; i <= demand1; i++)
                    pSub.SendNext(input.Next());

                var demand2 = (int)await pSub.ExpectRequestAsync();
                for (var i = 1; i <= demand2; i++)
                    pSub.SendNext(input.Next());

                var demand3 = (int)await pSub.ExpectRequestAsync();
                c.ExpectNext().Should().BeEquivalentTo(Enumerable.Range(1, demand1 + demand2));
                for (var i = 1; i <= demand3; i++)
                    pSub.SendNext(input.Next());

                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
                (await c.ExpectNextAsync())
                    .Should().BeEquivalentTo(Enumerable.Range(demand1 + demand2 + 1, demand3));
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
                await pSub.ExpectRequestAsync();

                var last = input.Next();
                pSub.SendNext(last);
                pSub.SendComplete();

                c.ExpectNext().Should().HaveCount(1).And.HaveElementAt(0, last);
                await c.ExpectCompleteAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            }, Materializer);
        }

        [Fact]
        public async Task A_GroupedWithin_must_deliver_buffered_elements_OnComplete_before_the_timeout()
        {
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.From(Enumerable.Range(1, 3))
                .GroupedWithin(1000, TimeSpan.FromSeconds(10))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var cSub = await c.ExpectSubscriptionAsync();
            cSub.Request(100);

            (await c.ExpectNextAsync()).Should().BeEquivalentTo(new[] { 1, 2, 3 });
            await c.ExpectCompleteAsync();
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task A_GroupedWithin_must_buffer_groups_until_requested_from_downstream()
        {
            var input = new Iterator<int>(Enumerable.Range(1, 10000));
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromSeconds(1))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var pSub = await p.ExpectSubscriptionAsync();
            var cSub = await c.ExpectSubscriptionAsync();

            cSub.Request(1);

            var demand1 = (int)await pSub.ExpectRequestAsync();
            for (var i = 1; i <= demand1; i++)
                pSub.SendNext(input.Next());
            (await c.ExpectNextAsync()).Should().BeEquivalentTo(Enumerable.Range(1, demand1));

            var demand2 = (int)await pSub.ExpectRequestAsync();
            for (var i = 1; i <= demand2; i++)
                pSub.SendNext(input.Next());
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            cSub.Request(1);
            (await c.ExpectNextAsync()).Should().BeEquivalentTo(Enumerable.Range(demand1 + 1, demand2));

            pSub.SendComplete();
            await c.ExpectCompleteAsync();
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task A_GroupedWithin_must_drop_empty_groups()
        {
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromMilliseconds(500))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var pSub = await p.ExpectSubscriptionAsync();
            var cSub = await c.ExpectSubscriptionAsync();

            cSub.Request(2);
            await pSub.ExpectRequestAsync();
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(600));

            pSub.SendNext(1);
            pSub.SendNext(2);
            (await c.ExpectNextAsync()).Should().BeEquivalentTo(new[] { 1, 2 });
            // nothing more requested
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(1100));
            cSub.Request(3);
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(600));
            pSub.SendComplete();
            await c.ExpectCompleteAsync();
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task A_GroupedWithin_must_not_emit_empty_group_when_finished_while_not_being_pushed()
        {
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromMilliseconds(50))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var pSub = await p.ExpectSubscriptionAsync();
            var cSub = await c.ExpectSubscriptionAsync();

            cSub.Request(1);
            await pSub.ExpectRequestAsync();
            pSub.SendComplete();
            await c.ExpectCompleteAsync();
        }

        // [Fact(Skip = "Skipped for async_testkit conversion build")]
        [Fact]
        public async Task A_GroupedWithin_must_reset_time_window_when_max_elements_reached()
        {
            var input = new Iterator<int>(Enumerable.Range(1, 10000));
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(upstream)
                .GroupedWithin(3, TimeSpan.FromSeconds(2))
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.RequestAsync(2);
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(1000));

            foreach (var _ in Enumerable.Range(1, 4))
                upstream.SendNext(input.Next());
            //Enumerable.Range(1, 4).ForEach(_ => upstream.SendNext(input.Next()));
            await downstream.WithinAsync(TimeSpan.FromMilliseconds(1000), async() =>
            {
                (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new[] { 1, 2, 3 });
                return NotUsed.Instance;
            });

            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(1500));

            await downstream.WithinAsync(TimeSpan.FromMilliseconds(1000), async() =>
            {
                (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new[] { 4 });
                return NotUsed.Instance;
            });

            upstream.SendComplete();
            await downstream.ExpectCompleteAsync();
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_GroupedWithin_must_group_early()
        {
            var random = new Random();
            var script = Script.Create(RandomTestRange(Sys).Select(_ =>
                {
                    var x = random.Next();
                    var y = random.Next();
                    var z = random.Next();

                    return ((ICollection<int>)new[] { x, y, z }, (ICollection<IEnumerable<int>>)new[] { new[] { x, y, z } });
                }).ToArray());

            RandomTestRange(Sys)
                .Select(async _ => await RunScriptAsync(script, Settings, flow => flow.GroupedWithin(3, TimeSpan.FromMinutes(10))));
        }

        [Fact]
        public void A_GroupedWithin_must_group_with_rest()
        {
            var random = new Random();
            Func<Script<int, IEnumerable<int>>> script = () =>
            {
                var i = random.Next();
                var rest = (new[] { i }, new[] { new[] { i } });

                return Script.Create(RandomTestRange(Sys).Select(_ =>
                {
                    var x = random.Next();
                    var y = random.Next();
                    var z = random.Next();

                    return ((ICollection<int>)new[] { x, y, z }, (ICollection<IEnumerable<int>>)new[] { new[] { x, y, z } });
                }).Concat(rest).ToArray());
            };

            RandomTestRange(Sys)
                .Select(async _ => await RunScriptAsync(script(), Settings, flow => flow.GroupedWithin(3, TimeSpan.FromMinutes(10))));
        }

        [Fact(Skip = "Skipped for async_testkit conversion build")]
        public void A_GroupedWithin_must_group_with_small_groups_with_backpressure()
        {
            var t = Source.From(Enumerable.Range(1, 10))
                .GroupedWithin(1, TimeSpan.FromDays(1))
                .Throttle(1, TimeSpan.FromMilliseconds(110), 0, ThrottleMode.Shaping)
                .RunWith(Sink.Seq<IEnumerable<int>>(), Materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => new List<int> { i }));
        }
    }

    public class FlowGroupedWeightedWithinSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowGroupedWeightedWithinSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_handle_handle_elements_larger_than_the_limit()
        {
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            Source.From(new List<int> { 1, 2, 3, 101, 4, 5, 6 })
                .GroupedWeightedWithin(100, TimeSpan.FromMilliseconds(100), t => t)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<int> { 1, 2, 3 });
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<int> { 101 });
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<int> { 4, 5, 6 });
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_not_drop_a_pending_last_element_on_upstream_finish()
        {
            var upstream = this.CreatePublisherProbe<long>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<long>>();

            Source.FromPublisher(upstream)
                .GroupedWeightedWithin(5, TimeSpan.FromMilliseconds(50), t => t)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.EnsureSubscriptionAsync();
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            await upstream.SendNextAsync(1);
            await upstream.SendNextAsync(2);
            await upstream.SendNextAsync(3);
            await upstream.SendCompleteAsync();
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 1, 2 });
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 3 });
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_append_zero_weighted_elements_to_a_full_group_before_timeout_received_if_downstream_hasnt_pulled_yet()
        {
            var upstream = this.CreatePublisherProbe<string>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<string>>();

            Source.FromPublisher(upstream)
                .GroupedWeightedWithin(5, TimeSpan.FromMilliseconds(50), t => t.Length)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.EnsureSubscriptionAsync();
            await upstream.SendNextAsync("333");
            await upstream.SendNextAsync("22");
            await upstream.SendNextAsync("");
            await upstream.SendNextAsync("");
            await upstream.SendNextAsync("");
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<string> { "333", "22", "", "", "" });
            await upstream.SendNextAsync("");
            await upstream.SendNextAsync("");
            await upstream.SendCompleteAsync();
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<string> { "", "" });
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_not_emit_an_empty_group_if_first_element_is_heavier_than_maxWeight()
        {
            var upstream = this.CreatePublisherProbe<long>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<long>>();

            Source.FromPublisher(upstream)
                .GroupedWeightedWithin(10, TimeSpan.FromMilliseconds(50), t => t)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.EnsureSubscriptionAsync();
            await downstream.RequestAsync(1);
            await upstream.SendNextAsync(11);
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 11 });
            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_handle_zero_cost_function_to_get_only_timed_based_grouping_without_limit()
        {
            var upstream = this.CreatePublisherProbe<string>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<string>>();

            Source.FromPublisher(upstream)
                .GroupedWeightedWithin(1L, TimeSpan.FromMilliseconds(100), _ => 0L)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.EnsureSubscriptionAsync();
            await downstream.RequestAsync(1);
            await upstream.SendNextAsync("333");
            await upstream.SendNextAsync("22");
            await upstream.SendNextAsync("333");
            await upstream.SendNextAsync("22");
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(50));
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<string> { "333", "22", "333", "22" });
            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_GroupedWeightedWithin_must_group_by_max_weight_and_max_number_of_elements_reached()
        {
            var upstream = this.CreatePublisherProbe<long>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<long>>();

            Source.FromPublisher(upstream)
                .GroupedWeightedWithin(10, 3, TimeSpan.FromSeconds(30), t => t)
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            await downstream.EnsureSubscriptionAsync();
            await upstream.SendNextAsync(1);
            await upstream.SendNextAsync(2);
            await upstream.SendNextAsync(3);
            await upstream.SendNextAsync(4);
            await upstream.SendNextAsync(5);
            await upstream.SendNextAsync(6);
            await upstream.SendNextAsync(11);
            await upstream.SendNextAsync(7);
            await upstream.SendNextAsync(2);
            await upstream.SendCompleteAsync();
            await downstream.RequestAsync(1);
            // split because of maxNumber: 3 element
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 1, 2, 3 });
            await downstream.RequestAsync(1);
            // split because of maxWeight: 9=4+5, one more element did not fit
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 4, 5 });
            await downstream.RequestAsync(1);
            // split because of maxWeight: 6, one more element did not fit
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 6 });
            await downstream.RequestAsync(1);
            // split because of maxWeight: 11
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 11 });
            await downstream.RequestAsync(1);
            // no split
            (await downstream.ExpectNextAsync()).Should().BeEquivalentTo(new List<long> { 7, 2 });
            await downstream.ExpectCompleteAsync();
        }
    }
}
