//-----------------------------------------------------------------------
// <copyright file="PulseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class PulseSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly TimeSpan _pulseInterval = TimeSpan.FromMilliseconds(20);

        [Fact]
        public void Pulse_should_signal_demand_once_every_interval()
        {
            var t = this.SourceProbe<int>()
                .Via(new Pulse<int>(Dilated(_pulseInterval)))
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var probe = t.Item1;
            var task = t.Item2;

            probe.SendNext(1);
            probe.ExpectNoMsg(_pulseInterval);
            probe.SendNext(2);
            probe.ExpectNoMsg(_pulseInterval);
            probe.SendComplete();

            task.AwaitResult().ShouldBeEquivalentTo(new[] { 1, 2 }, o => o.WithStrictOrdering());
        }

        [Fact]
        public void Pulse_should_keep_backpressure_if_there_is_no_demand_from_downstream()
        {
            var elements = Enumerable.Range(1, 10);
            var probe = Source.From(elements)
                .Via(new Pulse<int>(Dilated(_pulseInterval)))
                .RunWith(this.SinkProbe<int>(), Sys.Materializer());

            probe.EnsureSubscription();
            // lets waste some time without a demand and let pulse run its timer            
            probe.ExpectNoMsg(TimeSpan.FromTicks(_pulseInterval.Ticks * 10));

            probe.Request(elements.Count());
            foreach (var e in elements)
                probe.ExpectNext(e);
        }

        [Fact]
        public void Initially_opened_Pulse_should_emit_the_first_available_element()
        {
            var task = Source.Repeat(1)
                .Via(new Pulse<int>(Dilated(_pulseInterval), initiallyOpen: true))
                .InitialTimeout(Dilated(TimeSpan.FromMilliseconds(2)))
                .RunWith(Sink.First<int>(), Sys.Materializer());

            task.AwaitResult().Should().Be(1);
        }

        [Fact]
        public void Initially_opened_Pulse_should_signal_demand_once_every_interval()
        {
            var t = this.SourceProbe<int>()
                .Via(new Pulse<int>(Dilated(_pulseInterval), initiallyOpen: true))
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var probe = t.Item1;
            var task = t.Item2;

            probe.SendNext(1);
            probe.ExpectNoMsg(_pulseInterval);
            probe.SendNext(2);
            probe.ExpectNoMsg(_pulseInterval);
            probe.SendComplete();

            task.AwaitResult().ShouldBeEquivalentTo(new[] { 1, 2 }, o => o.WithStrictOrdering());
        }
    }
}
