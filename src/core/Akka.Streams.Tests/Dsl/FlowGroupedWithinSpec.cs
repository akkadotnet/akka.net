//-----------------------------------------------------------------------
// <copyright file="FlowGroupedWithinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
            this.AssertAllStagesStopped(() =>
            {
                var input = new Iterator<int>(Enumerable.Range(1, 10000));
                var p = this.CreateManualPublisherProbe<int>();
                var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

                Source.FromPublisher(p)
                    .GroupedWithin(1000, TimeSpan.FromSeconds(1))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);

                var pSub = p.ExpectSubscription();
                var cSub = c.ExpectSubscription();

                cSub.Request(100);

                var demand1 = (int)pSub.ExpectRequest();
                for (var i = 1; i <= demand1; i++)
                    pSub.SendNext(input.Next());

                var demand2 = (int)pSub.ExpectRequest();
                for (var i = 1; i <= demand2; i++)
                    pSub.SendNext(input.Next());

                var demand3 = (int)pSub.ExpectRequest();
                c.ExpectNext().ShouldAllBeEquivalentTo(Enumerable.Range(1, demand1 + demand2));
                for (var i = 1; i <= demand3; i++)
                    pSub.SendNext(input.Next());

                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                c.ExpectNext()
                    .ShouldAllBeEquivalentTo(Enumerable.Range(demand1 + demand2 + 1, demand3));
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                pSub.ExpectRequest();

                var last = input.Next();
                pSub.SendNext(last);
                pSub.SendComplete();

                c.ExpectNext().Should().HaveCount(1).And.HaveElementAt(0, last);
                c.ExpectComplete();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            }, Materializer);
        }

        [Fact]
        public void A_GroupedWithin_must_deliver_buffered_elements_OnComplete_before_the_timeout()
        {
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.From(Enumerable.Range(1, 3))
                .GroupedWithin(1000, TimeSpan.FromSeconds(10))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var cSub = c.ExpectSubscription();
            cSub.Request(100);

            c.ExpectNext().ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
            c.ExpectComplete();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void A_GroupedWithin_must_buffer_groups_until_requested_from_downstream()
        {
            var input = new Iterator<int>(Enumerable.Range(1, 10000));
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromSeconds(1))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var pSub = p.ExpectSubscription();
            var cSub = c.ExpectSubscription();

            cSub.Request(1);

            var demand1 = (int)pSub.ExpectRequest();
            for (var i = 1; i <= demand1; i++)
                pSub.SendNext(input.Next());
            c.ExpectNext().ShouldAllBeEquivalentTo(Enumerable.Range(1, demand1));

            var demand2 = (int)pSub.ExpectRequest();
            for (var i = 1; i <= demand2; i++)
                pSub.SendNext(input.Next());
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
            cSub.Request(1);
            c.ExpectNext().ShouldAllBeEquivalentTo(Enumerable.Range(demand1 + 1, demand2));

            pSub.SendComplete();
            c.ExpectComplete();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_GroupedWithin_must_drop_empty_groups()
        {
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromMilliseconds(500))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);
            
            var pSub = p.ExpectSubscription();
            var cSub = c.ExpectSubscription();

            cSub.Request(2);
            pSub.ExpectRequest();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(600));

            pSub.SendNext(1);
            pSub.SendNext(2);
            c.ExpectNext().ShouldAllBeEquivalentTo(new [] {1,2});
            // nothing more requested
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(1100));
            cSub.Request(3);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(600));
            pSub.SendComplete();
            c.ExpectComplete();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_GroupedWithin_must_not_emit_empty_group_when_finished_while_not_being_pushed()
        {
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(p)
                .GroupedWithin(1000, TimeSpan.FromMilliseconds(50))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var pSub = p.ExpectSubscription();
            var cSub = c.ExpectSubscription();

            cSub.Request(1);
            pSub.ExpectRequest();
            pSub.SendComplete();
            c.ExpectComplete();
        }
        
        [Fact]
        public void A_GroupedWithin_must_reset_time_window_when_max_elements_reached()
        {
            var input = new Iterator<int>(Enumerable.Range(1, 10000));
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(upstream)
                .GroupedWithin(3, TimeSpan.FromSeconds(2))
                .To(Sink.FromSubscriber(downstream))
                .Run(Materializer);

            downstream.Request(2);
            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(1000));

            Enumerable.Range(1,4).ForEach(_=>upstream.SendNext(input.Next()));
            downstream.Within(TimeSpan.FromMilliseconds(1000), () =>
            {
                downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] {1, 2, 3});
                return NotUsed.Instance;
            });

            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(1500));

            downstream.Within(TimeSpan.FromMilliseconds(1000), () =>
            {
                downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] {4});
                return NotUsed.Instance;
            });

            upstream.SendComplete();
            downstream.ExpectComplete();
            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
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

                    return ((ICollection<int>)new[] {x, y, z}, (ICollection<IEnumerable<int>>)new[] {new[] {x, y, z}});
                }).ToArray());

            RandomTestRange(Sys)
                .ForEach(_ => RunScript(script, Settings, flow => flow.GroupedWithin(3, TimeSpan.FromMinutes(10))));
        }

        [Fact]
        public void A_GroupedWithin_must_group_with_rest()
        {
            var random = new Random();
            Func<Script<int, IEnumerable<int>>> script = () =>
            {
                var i = random.Next();
                var rest = (new[] {i}, new[] {new[] {i}});

                return Script.Create(RandomTestRange(Sys).Select(_ =>
                {
                    var x = random.Next();
                    var y = random.Next();
                    var z = random.Next();

                    return ((ICollection<int>)new[] { x, y, z }, (ICollection<IEnumerable<int>>)new[] { new[] { x, y, z }});
                }).Concat(rest).ToArray());
            };

            RandomTestRange(Sys)
                .ForEach(_ => RunScript(script(), Settings, flow => flow.GroupedWithin(3, TimeSpan.FromMinutes(10))));
        }

        [Fact]
        public void A_GroupedWithin_must_group_with_small_groups_with_backpressure()
        {
            var t = Source.From(Enumerable.Range(1, 10))
                .GroupedWithin(1, TimeSpan.FromDays(1))
                .Throttle(1, TimeSpan.FromMilliseconds(110), 0, ThrottleMode.Shaping)
                .RunWith(Sink.Seq<IEnumerable<int>>(), Materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10).Select(i => new List<int> {i}));
        }
    }
}
