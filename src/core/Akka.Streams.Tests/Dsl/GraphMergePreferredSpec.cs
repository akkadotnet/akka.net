//-----------------------------------------------------------------------
// <copyright file="GraphMergePreferredSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMergePreferredSpec : TwoStreamsSetup<int>
    {
        public GraphMergePreferredSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new MergePreferredFixture(builder);

        private sealed class MergePreferredFixture : Fixture
        {
            public MergePreferredFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var merge = builder.Add(new MergePreferred<int>(1));
                Left = merge.Preferred;
                Right = merge.In(0);
                Out = merge.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void PreferredMerge_must_prefer_selected_input_more_than_others()
        {
            const int numElements = 10000;
            var preferred =
                Source.From(Enumerable.Range(1, numElements).Select(_ => 1))
                    .MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);
            var aux = Source.From(Enumerable.Range(1, numElements).Select(_ => 2))
                    .MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);

            var result = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var merge = b.Add(new MergePreferred<int>(3));
                b.From(preferred).To(merge.Preferred);
                b.From(merge.Out).Via(Flow.Create<int>().Grouped(numElements*2)).To(sink.Inlet);
                b.From(aux).To(merge.In(0));
                b.From(aux).To(merge.In(1));
                b.From(aux).To(merge.In(2));
                return ClosedShape.Instance;
            })).Run(Materializer);

            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.Count(x => x == 1).Should().Be(numElements);
        }

        [Fact]
        public void PreferredMerge_must_eventually_pass_through_all_elements_without_corrupting_the_ordering()
        {
            Source<int, Task<IEnumerable<int>>> Source(int from, int count) => Streams.Dsl.Source
                .From(Enumerable.Range(from, count))
                .MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);

            var result = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var merge = b.Add(new MergePreferred<int>(3));

                b.From(Source(1,100)).To(merge.Preferred);
                b.From(merge.Out).Via(Flow.Create<int>().Grouped(500)).To(sink.Inlet);
                b.From(Source(101, 100)).To(merge.In(0));
                b.From(Source(201, 100)).To(merge.In(1));
                b.From(Source(301, 100)).To(merge.In(2));

                return ClosedShape.Instance;
            })).Run(Materializer);

            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            result.Result.Where(i => i <= 100).ShouldOnlyContainInOrder(Enumerable.Range(1, 100));
            result.Result.Where(i => i > 100 && i <= 200).ShouldOnlyContainInOrder(Enumerable.Range(101, 100));
            result.Result.Where(i => i > 200 && i <= 300).ShouldOnlyContainInOrder(Enumerable.Range(201, 100));
            result.Result.Where(i => i > 300 && i <= 400).ShouldOnlyContainInOrder(Enumerable.Range(301, 100));

            result.Result.ShouldBeEquivalentTo(Enumerable.Range(1, 400));
        }

        [Fact]
        public void PreferredMerge_must_disallow_multiple_preferred_inputs()
        {
            var s = Source.From(Enumerable.Range(0, 4));
            Action action = () =>
            {
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var merge = b.Add(new MergePreferred<int>(1));
                    b.From(s).To(merge.Preferred);
                    b.From(s).To(merge.Preferred);
                    b.From(s).To(merge.In(0));
                    b.From(merge.Out).To(Sink.First<int>().MapMaterializedValue(_ => NotUsed.Instance));
                    return ClosedShape.Instance;
                }));
            };

            action.ShouldThrow<ArgumentException>()
                .And.Message.Should()
                .Contain("The input port [MergePreferred.preferred] is already connected");
        }
    }
}
