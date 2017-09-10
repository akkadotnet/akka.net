//-----------------------------------------------------------------------
// <copyright file="GraphMergePrioritizedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMergePrioritizedSpec : TwoStreamsSetup<int>
    {
        public GraphMergePrioritizedSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new MergePrioritizedFixture(builder);

        private sealed class MergePrioritizedFixture : Fixture
        {
            public MergePrioritizedFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var mergePrioritized = builder.Add(new MergePrioritized<int>(new List<int> { 2, 8 }));
                Left = mergePrioritized .In(0);
                Right = mergePrioritized .In(1);
                Out = mergePrioritized .Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void MergePrioritized_must_stream_data_from_all_sources()
        {
            var source1 = Source.FromEnumerator(() => Enumerable.Range(1, 3).GetEnumerator());
            var source2 = Source.FromEnumerator(() => Enumerable.Range(4, 6).GetEnumerator());
            var source3 = Source.FromEnumerator(() => Enumerable.Range(7, 9).GetEnumerator());

            var priorities = new List<int> { 6, 3, 1 };
            var probe = this.CreateManualSubscriberProbe<int>();

            ThreeSourceMerge<int>(source1, source2, source3, priorities, probe).Run(this.Materializer());

            var subscription = probe.ExpectSubscription();

            var collected = new HashSet<int>();
            for (int i = 1; i <= 9; i++)
            {
                subscription.Request(1);
                collected.Add(probe.ExpectNext());
            }

            collected.Should().BeEquivalentTo(new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            probe.ExpectComplete();
        }

        [Fact]
        public void MergePrioritized_must_stream_data_with_priority()
        {
            var elementCount = 20000;
            var source1 = Source.FromEnumerator(() => Vector.Fill<int>(elementCount)(() => 1).GetEnumerator());
            var source2 = Source.FromEnumerator(() => Vector.Fill<int>(elementCount)(() => 2).GetEnumerator());
            var source3 = Source.FromEnumerator(() => Vector.Fill<int>(elementCount)(() => 3).GetEnumerator());

            var priorities = new List<int> { 6, 3, 1 };
            var probe = this.CreateManualSubscriberProbe<int>();

            ThreeSourceMerge<int>(source1, source2, source3, priorities, probe).Run(this.Materializer());

            var subscription = probe.ExpectSubscription();

            var collected = new HashSet<int>();
            for (int i = 1; i <= elementCount; i++)
            {
                subscription.Request(1);
                collected.Add(probe.ExpectNext());
            }

            double ones = collected.Count(x => x == 1);
            double twos = collected.Count(x => x == 2);
            double threes = collected.Count(x => x == 3);

            Math.Round(ones / twos).Should().Be(2);
            Math.Round(ones / threes).Should().Be(6);
            Math.Round(twos / threes).Should().Be(3);
        }

        private RunnableGraph<T> ThreeSourceMerge<T>(Source<T, NotUsed> source1, Source<T, NotUsed> source2,
            Source<T, NotUsed> source3, List<int> priorities, TestSubscriber.ManualProbe<T> probe)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(source1, source2, source3, (p1, p2, p3) => { }, (builder, s1, s2, s3) =>
            {
                var merge = builder.Add(new MergePrioritized<T>(priorities));
                builder.From(s1.Outlet).To(merge.In(0));
                builder.From(s2.Outlet).To(merge.In(1));
                builder.From(s3.Outlet).To(merge.In(2));

                builder.From(merge.Out).To(Sink.FromSubscriber(probe));

                return ClosedShape.Instance;
            }));
        }
    }
}
