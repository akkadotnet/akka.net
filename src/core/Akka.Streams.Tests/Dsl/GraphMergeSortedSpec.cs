//-----------------------------------------------------------------------
// <copyright file="GraphMergeSortedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMergeSortedSpec : TwoStreamsSetup<int>
    {
        public GraphMergeSortedSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new MergeSortedFixture(builder);

        private sealed class MergeSortedFixture : Fixture
        {
            public MergeSortedFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var merge = builder.Add(new MergeSorted<int>((l, r) => l.CompareTo(r)));
                Left = merge.In0;
                Right = merge.In1;
                Out = merge.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void MergeSorted_must_work_in_the_nominal_case()
        {
            var random = new Random();
            var gen = Enumerable.Range(1, 10)
                .Select(i => Enumerable.Range(1, 1000)
                    .Select(_ => random.Next(1, 3) == 2).ToList());
            foreach (var picks in gen)
            {
                var n = picks.Count;
                var group = picks.Select((b, i) => (b, i)).GroupBy(t => t.Item1).ToList();
                var left = group[0].ToList();
                var right = group[1].ToList();
                var task = Source.From(left.Select(t => t.Item2))
                    .MergeSorted(Source.From(right.Select(t => t.Item2)))
                    .Grouped(Math.Max(n, 1))
                    .Concat(Source.Single<IEnumerable<int>>(new List<int>()))
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.AwaitResult().ShouldBeEquivalentTo(Enumerable.Range(0, n), o => o.WithStrictOrdering());
            }
        }

        [Fact]
        public void MergeSorted_must_work_with_custom_comparer()
        {
            var task = Source.From(new[] { 1, 5 })
                    .MergeSorted(Source.From(new[] { 0, 1, 2, 7 }), (l, r) => 2 * l.CompareTo(r))
                    .RunWith(Sink.Seq<int>(), Materializer);

            task.AwaitResult().ShouldBeEquivalentTo(new[] { 0, 1, 1, 2, 5, 7 }, o => o.WithStrictOrdering());
        }
    }
}
