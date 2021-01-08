//-----------------------------------------------------------------------
// <copyright file="RequestStrategySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Streams.Actors;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Actor
{
    public class RequestStrategySpecs
    {
        [Fact]
        public void Provided_RequestStrategies_should_implement_OneByOne_correctly()
        {
            var strat = OneByOneRequestStrategy.Instance;
            strat.RequestDemand(0).Should().Be(1);
            strat.RequestDemand(1).Should().Be(0);
            strat.RequestDemand(2).Should().Be(0);
        }

        [Fact]
        public void Provided_RequestStrategies_should_implement_Zero_correctly()
        {
            var strat = ZeroRequestStrategy.Instance;
            strat.RequestDemand(0).Should().Be(0);
            strat.RequestDemand(1).Should().Be(0);
            strat.RequestDemand(2).Should().Be(0);
        }

        [Fact]
        public void Provided_RequestStrategies_should_implement_Watermark_correctly()
        {
            var strat = new WatermarkRequestStrategy(highWatermark: 10);
            strat.RequestDemand(0).Should().Be(10);
            strat.RequestDemand(9).Should().Be(0);
            strat.RequestDemand(6).Should().Be(0);
            strat.RequestDemand(5).Should().Be(0);
            strat.RequestDemand(4).Should().Be(6);
        }

        [Fact]
        public void Provided_RequestStrategies_should_implement_MaxInFlight_with_batchSize_1_correctly()
        {
            var queue = new List<string>();
            var strat = new InFlightWithBatchSize(10, queue, 1);
            strat.RequestDemand(0).Should().Be(10);
            strat.RequestDemand(9).Should().Be(1);
            queue.Add("a");
            strat.RequestDemand(0).Should().Be(9);
            strat.RequestDemand(8).Should().Be(1);
            strat.RequestDemand(9).Should().Be(0);
            queue.Add("b");
            queue.Add("c");
            strat.RequestDemand(5).Should().Be(2);
            queue.AddRange(new []{"d", "e", "f", "g", "h", "i", "j"});
            queue.Count.Should().Be(10);
            strat.RequestDemand(0).Should().Be(0);
            strat.RequestDemand(1).Should().Be(0);
            queue.Add("g");
            strat.RequestDemand(0).Should().Be(0);
            strat.RequestDemand(1).Should().Be(0);
        }

        [Fact]
        public void Provided_RequestStrategies_should_implement_MaxInFlight_with_batchSize_3_correctly()
        {
            var queue = new List<string>();
            var strat = new InFlightWithBatchSize(10, queue, 3);
            strat.RequestDemand(0).Should().Be(10);
            queue.Add("a");
            strat.RequestDemand(9).Should().Be(0);
            queue.Add("b");
            strat.RequestDemand(8).Should().Be(0);
            queue.Add("c");
            strat.RequestDemand(7).Should().Be(0);
            queue.Add("d");
            strat.RequestDemand(6).Should().Be(0);
            queue.Remove("a"); //3 remaining in queue
            strat.RequestDemand(6).Should().Be(0);
            queue.Remove("b"); //2 remaining in queue
            strat.RequestDemand(6).Should().Be(0);
            queue.Remove("c"); //1 remaining in queue
            strat.RequestDemand(6).Should().Be(3);
        }

        [Fact]
        public void Provided_RequestStrategies_should_implement_MaxInFlight_with_batchSize_max_correctly()
        {
            var queue = new List<string>();
            var strat = new InFlightWithBatchSize(max: 3, queue: queue, batchSize: 5 ); //will be bounded to max
            strat.RequestDemand(0).Should().Be(3);
            queue.Add("a");
            strat.RequestDemand(2).Should().Be(0);
            queue.Add("b");
            strat.RequestDemand(1).Should().Be(0);
            queue.Add("c");
            strat.RequestDemand(0).Should().Be(0);
            queue.Remove("a");
            strat.RequestDemand(0).Should().Be(0);
            queue.Remove("b");
            strat.RequestDemand(0).Should().Be(0);
            queue.Remove("c");
            strat.RequestDemand(0).Should().Be(3);
        }
    }

    internal class InFlightWithBatchSize : MaxInFlightRequestStrategy
    {
        private readonly List<string> _queue;

        public InFlightWithBatchSize(int max, List<string> queue, int batchSize) : base(max)
        {
            _queue = queue;
            BatchSize = batchSize;
        }

        public override int BatchSize { get; }

        public override int InFlight => _queue.Count;
    }
}
