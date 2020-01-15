//-----------------------------------------------------------------------
// <copyright file="KeepAliveConcatSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class KeepAliveConcatSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly Source<IEnumerable<int>, NotUsed> _sampleSource = Source.From(Enumerable.Range(1, 10).Grouped(3));

        private IEnumerable<IEnumerable<int>> Expand(IEnumerable<int> lst)
        {
            return lst.Select(x => new[] { x });
        }

        [Fact]
        public void KeepAliveConcat_should_not_emit_additional_elements_if_upstream_is_fast_enough()
        {
            var t = _sampleSource
                .Via(new KeepAliveConcat<IEnumerable<int>>(5, TimeSpan.FromSeconds(1), Expand))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<IEnumerable<int>>>(), Sys.Materializer());

            t.AwaitResult()
                .SelectMany(x => x)
                .ShouldBeEquivalentTo(Enumerable.Range(1, 10), o => o.WithStrictOrdering());
        }

        [Fact]
        public void KeepAliveConcat_should_emit_elements_periodically_after_silent_periods()
        {
            var sourceWithIdleGap = Source.From(Enumerable.Range(1, 5).Grouped(3))
                .Concat
                (
                    Source.From(Enumerable.Range(6, 5).Grouped(3)).InitialDelay(TimeSpan.FromSeconds(2))
                );

            var t = sourceWithIdleGap
                .Via(new KeepAliveConcat<IEnumerable<int>>(5, TimeSpan.FromSeconds(0.6), Expand))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<IEnumerable<int>>>(), Sys.Materializer());

            t.AwaitResult()
                .SelectMany(x => x)
                .ShouldBeEquivalentTo(Enumerable.Range(1, 10), o => o.WithStrictOrdering());
        }

        [Fact]
        public void KeepAliveConcat_should_immediately_pull_upstream()
        {
            var upstream = this.CreatePublisherProbe<IEnumerable<int>>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            Source.FromPublisher(upstream)
                .Via(new KeepAliveConcat<IEnumerable<int>>(2, TimeSpan.FromSeconds(1), Expand))
                .RunWith(Sink.FromSubscriber(downstream), Sys.Materializer());

            downstream.Request(1);

            upstream.SendNext(new[] { 1 });
            downstream.ExpectNext().ShouldBeEquivalentTo(new[] { 1 });

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAliveConcat_should_immediately_pull_upstream_after_busy_period()
        {
            var upstream = this.CreatePublisherProbe<IEnumerable<int>>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            _sampleSource.Concat(Source.FromPublisher(upstream))
                .Via(new KeepAliveConcat<IEnumerable<int>>(2, TimeSpan.FromSeconds(1), Expand))
                .RunWith(Sink.FromSubscriber(downstream), Sys.Materializer());

            downstream.Request(10);

            var actual = downstream.ExpectNextN(6);
            var expected = Enumerable.Range(1, 3).Grouped(1).Concat(Enumerable.Range(4, 7).Grouped(3));
            actual.ShouldBeEquivalentTo(expected, o => o.WithStrictOrdering());

            downstream.Request(1);

            upstream.SendNext(new[] { 1 });
            downstream.ExpectNext().ShouldBeEquivalentTo(new[] { 1 });

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAliveConcat_should_work_if_timer_fires_before_initial_request_after_busy_period()
        {
            var upstream = this.CreatePublisherProbe<IEnumerable<int>>();
            var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();

            _sampleSource.Concat(Source.FromPublisher(upstream))
                .Via(new KeepAliveConcat<IEnumerable<int>>(2, TimeSpan.FromSeconds(1), Expand))
                .RunWith(Sink.FromSubscriber(downstream), Sys.Materializer());

            downstream.Request(10);

            var actual = downstream.ExpectNextN(6);
            var expected = Enumerable.Range(1, 3).Grouped(1).Concat(Enumerable.Range(4, 7).Grouped(3));
            actual.ShouldBeEquivalentTo(expected, o => o.WithStrictOrdering());

            downstream.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
            downstream.Request(1);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAliveConcat_should_emit_buffered_elements_when_upstream_completed()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(upstream)
                .Via(new KeepAliveConcat<int>(5, TimeSpan.FromSeconds(60), x => new[] { x }))
                .RunWith(Sink.FromSubscriber(downstream), Sys.Materializer());

            upstream.SendNext(1);
            upstream.SendNext(2);            
            upstream.SendComplete();

            downstream.Request(2);
            downstream.ExpectNextN(2).ShouldBeEquivalentTo(new[] { 1, 2 }, o => o.WithStrictOrdering());

            downstream.Request(1);
            downstream.ExpectComplete();
        }
    }

    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Grouped<T>(this IEnumerable<T> enumerable, int n)
        {
            return enumerable
                .Select((x, i) => new { X = x, I = i })
                .GroupBy(g => g.I / n)
                .Select(g => g.Select(p => p.X));
        }
    }
}
