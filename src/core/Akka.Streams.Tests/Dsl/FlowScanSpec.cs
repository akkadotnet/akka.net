//-----------------------------------------------------------------------
// <copyright file="FlowScanSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowScanSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowScanSpec(ITestOutputHelper helper):base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private IEnumerable<int> Scan(Source<int, NotUsed> source, TimeSpan? duration = null)
        {
            duration = duration ?? TimeSpan.FromSeconds(5);

            var t = source.Scan(0, (i, i1) => i + i1).RunAggregate(new List<int>(), (list, i) =>
            {
                list.Add(i);
                return list;
            }, Materializer);

            t.Wait(duration.Value).Should().BeTrue();
            return t.Result;
        }
        
        [Fact]
        public void A_Scan_must_Scan()
        {
            Func<int[], int[]> scan = source =>
            {
                var result = new int[source.Length+1];
                result[0] = 0;

                for (var i = 1; i <= source.Length; i++)
                    result[i] = result[i - 1] + source[i - 1];

                return result;
            };

            this.AssertAllStagesStopped(() =>
            {
                var random = new Random();
                var v = Enumerable.Range(1, random.Next(100, 1000)).Select(_ => random.Next()).ToArray();
                Scan(Source.From(v)).ShouldAllBeEquivalentTo(scan(v));
            }, Materializer);
        }

        [Fact]
        public void A_Scan_must_Scan_empty_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("fail!");
                Action fail = () => Scan(Source.Failed<int>(error));
                fail.ShouldThrow<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Scan_must_Scan_empty() =>
            this.AssertAllStagesStopped(() => Scan(Source.Empty<int>()).ShouldAllBeEquivalentTo(new[] {0}), Materializer);

        [Fact]
        public void A_Scan_must_emit_values_promptly()
        {
            var task = Source.Single(1).MapMaterializedValue<TaskCompletionSource<int>>(_ => null)
                .Concat(Source.Maybe<int>())
                .Scan(0, (i, i1) => i + i1)
                .Take(2)
                .RunWith(Sink.Seq<int>(), Materializer);

            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {0, 1});
        }

        [Fact]
        public void A_Scan_must_restart_properly()
        {
            var scan = Flow.Create<int>().Scan(0, (old, current) =>
            {
                if (current <= 0)
                    throw new ArgumentException("current must be greater than zero");

                return old + current;
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider));

            Source.From(new[] {1, 3, -1, 5, 7})
                .Via(scan)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .ToStrict(TimeSpan.FromSeconds(1))
                .ShouldAllBeEquivalentTo(new[] {0, 1, 4, 0, 5, 12});

        }

        [Fact]
        public void A_Scan_must_resume_properly()
        {
            var scan = Flow.Create<int>().Scan(0, (old, current) =>
            {
                if (current <= 0)
                    throw new ArgumentException("current must be greater than zero");

                return old + current;
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider));

            Source.From(new[] {1, 3, -1, 5, 7})
                .Via(scan)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .ToStrict(TimeSpan.FromSeconds(1))
                .ShouldAllBeEquivalentTo(new[] {0, 1, 4, 9, 16});
        }
        
        [Fact]
        public void A_Scan_must_scan_normally_for_empty_source()
        {
            Source.Empty<int>()
                .Scan(0, (i, i1) => i + i1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(2)
                .ExpectNext(0)
                .ExpectComplete();
        }

        [Fact]
        public void A_Scan_must_fail_when_upstream_failed()
        {
            var cause = new TestException("");
            Source.Failed<int>(cause)
                .Scan(0, (i, i1) => i + i1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(2)
                .ExpectError().Should().Be(cause);
        }
    }
}
