using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowFilterSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }

        public FlowFilterSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
        }

        [Fact]
        public void A_Filter_must_filter()
        {
            var random = new Random();
            Script<int, int> script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next();
                return new Tuple<ICollection<int>, ICollection<int>>(new[] {x}, (x & 1) == 0 ? new[] {x} : new int[] {});
            }).ToArray());

            RandomTestRange(Sys).ForEach(_ => RunScript(script, Settings, flow => flow.Filter(x => x%2 == 0)));
        }

        [Fact]
        public void A_Filter_must_not_blow_up_with_high_request_counts()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            var materializer = ActorMaterializer.Create(Sys, settings);

            var probe = TestSubscriber.CreateManualProbe<int>(this);
            Source.From(Enumerable.Repeat(0, 1000).Concat(new[] {1}))
                .Filter(x => x != 0)
                .RunWith(Sink.FromSubscriber(probe), materializer);

            var subscription = probe.ExpectSubscription();
            for (var i = 1; i <= 1000; i++)
                subscription.Request(int.MaxValue);

            probe.ExpectNext(1);
            probe.ExpectComplete();
        }

        [Fact]
        public void A_FilterNot_must_filter_based_on_inverted_predicate()
        {
            var random = new Random();
            Script<int, int> script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next();
                return new Tuple<ICollection<int>, ICollection<int>>(new[] { x }, (x & 1) == 1 ? new[] { x } : new int[] { });
            }).ToArray());

            RandomTestRange(Sys).ForEach(_ => RunScript(script, Settings, flow => flow.FilterNot(x => x % 2 == 0)));
        }
    }
}
