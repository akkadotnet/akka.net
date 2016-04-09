using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDropSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowDropSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public void A_Drop_must_drop()
        {
            Func<long, Script<int, int>> script =
                d => Script.Create(RandomTestRange(Sys)
                            .Select(n => new Tuple<ICollection<int>, ICollection<int>>(new[] {n}, n <= d ? new int[] { } : new[] {n}))
                            .ToArray());
            var random = new Random();
            foreach (var _ in RandomTestRange(Sys))
            {
                var d = Math.Min(Math.Max(random.Next(-10, 60), 0), 50);
                RunScript(script(d), Settings, f => f.Drop(d));
            }
        }

        [Fact]
        public void A_Drop_must_not_drop_anything_for_negative_n()
        {
            var probe = TestSubscriber.CreateManualProbe<int>(this);
            Source.From(new[] {1, 2, 3}).Drop(-1).To(Sink.FromSubscriber(probe)).Run(Materializer);
            probe.ExpectSubscription().Request(10);
            probe.ExpectNext(1);
            probe.ExpectNext(2);
            probe.ExpectNext(3);
            probe.ExpectComplete();
        }
    }
}
