//-----------------------------------------------------------------------
// <copyright file="DelayFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class DelayFlowSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void DelayFlow_should_work_with_empty_source()
        {
            Source.Empty<int>()
                .Via(new DelayFlow<int>(TimeSpan.Zero))
                .RunWith(this.SinkProbe<int>(), Sys.Materializer())
                .Request(1)
                .ExpectComplete();
        }

        [Fact(Skip = "Racy - timing is rather sensitive on Azure DevOps")]
        public void DelayFlow_should_work_with_fixed_delay()
        {
            var fixedDelay = TimeSpan.FromSeconds(1);
            var elems = Enumerable.Range(1, 10);

            var probe = Source.From(elems)
                .Select(_ => DateTime.Now.Ticks)
                .Via(new DelayFlow<long>(fixedDelay))
                .Select(start => DateTime.Now.Ticks - start)
                .RunWith(this.SinkProbe<long>(), Sys.Materializer());

            foreach (var e in elems)
            {
                var next = probe
                  .Request(1)
                  .ExpectNext(fixedDelay + Dilated(fixedDelay));

                next.Should().BeGreaterOrEqualTo(fixedDelay.Ticks);
            }

            probe.ExpectComplete();
        }

        [Fact]
        public void DelayFlow_should_work_without_delay()
        {
            var elems = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 };

            Source.From(elems)
                .Via(new DelayFlow<int>(TimeSpan.Zero))
                .RunWith(this.SinkProbe<int>(), Sys.Materializer())
                .Request(elems.Length)
                .ExpectNextN(elems)
                .ExpectComplete();
        }

        [Fact(Skip ="Racy")]
        public void DelayFlow_should_work_with_linear_increasing_delay()
        {
            var elems = Enumerable.Range(1, 10);
            var step = TimeSpan.FromSeconds(1);
            var initial = TimeSpan.FromSeconds(1);
            var max = TimeSpan.FromSeconds(5);

            bool incWhile((int, long) i)
            {
                return i.Item1 < 7;
            }

            var probe = Source.From(elems)
                .Select(e => (e, DateTime.Now.Ticks))
                .Via(new DelayFlow<(int, long)>(
                    () => new LinearIncreasingDelay<(int, long)>(step, incWhile, initial, max))
                )
                .Select(pair => DateTime.Now.Ticks - pair.Item2)
                .RunWith(this.SinkProbe<long>(), Sys.Materializer());

            foreach (var e in elems)
            {
                if (incWhile((e, 1L)))
                {
                    var afterIncrease = initial + TimeSpan.FromTicks(step.Ticks * e);
                    var delay = afterIncrease < max ? afterIncrease : max;
                    var next = probe
                        .Request(1)
                        .ExpectNext(delay + Dilated(delay));

                    next.Should().BeGreaterOrEqualTo(delay.Ticks);
                }
                else
                {
                    var next = probe
                        .Request(1)
                        .ExpectNext(initial + Dilated(initial));

                    next.Should().BeGreaterOrEqualTo(initial.Ticks);
                }
            }
            probe.ExpectComplete();
        }
    }
}
