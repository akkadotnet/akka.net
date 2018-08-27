//-----------------------------------------------------------------------
// <copyright file="TimeWindowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class TimeWindowSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly TimeSpan _timeWindow = TimeSpan.FromMilliseconds(100);
        private readonly TimeSpan _epsilonTime = TimeSpan.FromMilliseconds(10);

        public TimeWindowSpec()
            : base(DefaultConfig.WithFallback("akka.scheduler.implementation = \"Akka.TestKit.TestScheduler, Akka.TestKit\""))
        {
        }

        private TestScheduler Scheduler => (TestScheduler)Sys.Scheduler;

        [Fact]
        public void TimeWindow_flow_should_aggregate_data_for_predefined_amount_of_time()
        {
            var summingWindow = TimeWindow.Create<int, int>(_timeWindow, x => x, (x, y) => x + y, eager: false);

            var sub = Source.Repeat(1)
                .Via(summingWindow)
                .RunWith(this.SinkProbe<int>(), Sys.Materializer());

            sub.Request(2);

            sub.ExpectNoMsg(_timeWindow + _epsilonTime);
            Scheduler.Advance(_timeWindow + _epsilonTime);
            sub.ExpectNext();

            sub.ExpectNoMsg(_timeWindow + _epsilonTime);
            Scheduler.Advance(_timeWindow + _epsilonTime);
            sub.ExpectNext();
        }

        [Fact]
        public void TimeWindow_flow_should_emit_the_first_seed_if_eager()
        {
            var summingWindow = TimeWindow.Create<int, int>(_timeWindow, x => x, (x, y) => x + y, eager: true);

            var sub = Source.Repeat(1)
                .Via(summingWindow)
                .RunWith(this.SinkProbe<int>(), Sys.Materializer());

            sub.Request(2);

            sub.ExpectNext();

            sub.ExpectNoMsg(_timeWindow + _epsilonTime);
            Scheduler.Advance(_timeWindow + _epsilonTime);
            sub.ExpectNext();
        }
    }
}
