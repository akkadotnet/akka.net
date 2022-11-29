//-----------------------------------------------------------------------
// <copyright file="RepeatPreviousSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class ReuseLatestSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public ReuseLatestSpec(ITestOutputHelper testOutputHelper) : base(Config.Empty, output: testOutputHelper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }
        
        [Fact]
        public async Task RepeatPrevious_should_immediately_terminate_with_Empty_source()
        {
            var source = Source.Empty<int>();
            var result = await source.RepeatPrevious().RunWith(Sink.Seq<int>(), Materializer);
            result.Should().BeEmpty();
        }
        
        [Fact]
        public async Task RepeatPrevious_should_complete_when_upstream_completes()
        {
            var source = Source.Single(1).RepeatPrevious();
            var result = await source.RunWith(Sink.Seq<int>(), Materializer);

            // as a side-effect of RepeatPrevious' buffering process, there's going to be an extra element in the result
            result.Should().BeEquivalentTo(1, 1);
        }

        [Fact]
        public async Task RepeatPrevious_should_fail_when_upstream_fails()
        {
            Func<Task> Exec() => async () =>
            {
                var source = Source.From(Enumerable.Range(0,9)).Where(i =>
                {
                    if (i % 5 == 0)
                    {
                        throw new ApplicationException("failed");
                    }

                    return true;
                }).RepeatPrevious();
                var result = await source.RunWith(Sink.Seq<int>(), Materializer);
            };
            
            await Exec().Should().ThrowAsync<ApplicationException>();
        }

        [Fact]
        public async Task RepeatPrevious_should_repeat_when_no_newValues_available()
        {
            // <RepeatPrevious>
            var (queue, source) = Source.Queue<int>(10, OverflowStrategy.Backpressure).PreMaterialize(Materializer);
            
            // populate 1 into queue
            await queue.OfferAsync(1);

            // take 4 items from the queue
            var result = await source.RepeatPrevious().Take(4).RunWith(Sink.Seq<int>(), Materializer);
            
            // the most recent queue item will be repeated 3 times, plus the original element
            result.Should().BeEquivalentTo(1,1,1,1);
            // </RepeatPrevious>
        }
    }
}