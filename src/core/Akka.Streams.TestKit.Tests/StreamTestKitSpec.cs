//-----------------------------------------------------------------------
// <copyright file="StreamTestKitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    public class StreamTestKitSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public StreamTestKitSpec(ITestOutputHelper output = null) : base(output)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private Exception Ex() => new TestException("Boom!");

        [Fact]
        public async Task TestSink_Probe_ToStrictAsync()
        {
            (await Source.From(Enumerable.Range(1, 4))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .ToStrictAsync(TimeSpan.FromMilliseconds(300)))
                .Should()
                .Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ToStrictAsync_with_failing_source()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.From(Enumerable.Range(1, 3).Select(i =>
                    {
                        if (i == 3)
                            throw Ex();
                        return i;
                    })).RunWith(this.SinkProbe<int>(), Materializer)
                    .ToStrictAsync(TimeSpan.FromMilliseconds(300));
            });

            var aggregateException = error.InnerException;
            aggregateException.InnerException.Message.Should().Contain("Boom!");
            error.Message.Should().Contain("1, 2");
        }

        [Fact]
        public async Task TestSink_Probe_ToStrictAsync_when_subscription_was_already_obtained()
        {
            var p = Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer);
            await p.ExpectSubscriptionAsync();
            (await p.ToStrictAsync(TimeSpan.FromMilliseconds(300))).Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_with_right_element()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrErrorAsync(1, Ex()).Task;
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_with_right_exception()
        {
            await Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrErrorAsync(1, Ex()).Task;
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_fail_if_the_next_element_is_not_the_expected_one()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(4)
                    .ExpectNextOrErrorAsync(100, Ex()).Task;
            });
            error.Message.Should().Contain("OnNext(100)");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectErrorAsync()
        {
            (await Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .ExpectErrorAsync()).Should().Be(Ex());
        }

        [Fact]
        public async Task TestSink_Probe_ExpectErrorAsync_fail_if_no_error_signalled()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectErrorAsync();
            });
            error.Message.Should().Contain("OnNext");
        }

        [Fact]
        public void TestSink_Probe_ExpectCompleteAsync_should_fail_if_error_signalled()
        {
            var error = Record.Exception(() =>
            {
                Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            });
            error.Message.Should().Contain("OnError");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectCompleteAsync_should_fail_if_next_element_signalled()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectCompleteAsync().Task;
            });
            error.Message.Should().Contain("OnNext");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrCompleteAsync_with_right_element()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrCompleteAsync(1).Task;
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrCompleteAsync_with_completion()
        {
            await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrCompleteAsync(1)
                .ExpectNextOrCompleteAsync(1337).Task;
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextAsync_should_pass_with_right_element()
        {
            (await Source.Single(1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .ExpectNextAsync<int>(i => i == 1))
                .ShouldBe(1);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextPredicateAsync_should_fail_with_wrong_element()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.Single(1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectNextAsync<int>(i => i == 2);
            });
            error.Message.ShouldStartWith("Got a message of the expected type");
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_pass_with_right_element()
        {
            await Source.Single(1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .MatchNextAsync<int>(i => i == 1).Task;
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_allow_to_chain_test_methods()
        {
            await Source.From(Enumerable.Range(1, 2))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(2)
                .MatchNextAsync<int>(i => i == 1)
                .ExpectNextAsync(2).Task;
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_fail_with_wrong_element()
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await Source.Single(1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .MatchNextAsync<int>(i => i == 2).Task;
            });
            error.Message.ShouldStartWith("Got a message of the expected type");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextNAsync_given_a_number_of_elements()
        {
            (await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextNAsync(4).ToListAsync()).Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextNAsync_given_specific_elements()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextNAsync(new[] {1, 2, 3, 4}).Task;
        }
    }
}
