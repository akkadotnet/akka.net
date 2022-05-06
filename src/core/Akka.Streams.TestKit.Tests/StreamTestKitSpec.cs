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
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

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
            var result = await Source.From(Enumerable.Range(1, 4))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .ToStrictAsync(TimeSpan.FromMilliseconds(300))
                .ToListAsync();
            result.Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ToStrictAsync_with_failing_source()
        {
            var err = await Awaiting(async () =>
                {
                    await Source.From(Enumerable.Range(1, 3).Select(i =>
                        {
                            if (i == 3)
                                throw Ex();
                            return i;
                        })).RunWith(this.SinkProbe<int>(), Materializer)
                        .AsyncBuilder()
                        .ToStrictAsync(TimeSpan.FromMilliseconds(300))
                        .ToListAsync();
                })
                .Should().ThrowAsync<ArgumentException>();

            var error = err.Subject.First();
            var aggregateException = error.InnerException;
            aggregateException.InnerException.Message.Should().Contain("Boom!");
            error.Message.Should().Contain("1, 2");
        }

        [Fact]
        public async Task TestSink_Probe_ToStrictAsync_when_subscription_was_already_obtained()
        {
            var result = await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .EnsureSubscription()
                .ToStrictAsync(3.Seconds())
                .ToListAsync();
            result.Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_with_right_element()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(4)
                .ExpectNextOrError(1, Ex())
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_with_right_exception()
        {
            await Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(4)
                .ExpectNextOrError(1, Ex())
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrErrorAsync_fail_if_the_next_element_is_not_the_expected_one()
        {
            await Awaiting(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(4)
                    .ExpectNextOrError(100, Ex())
                    .ExecuteAsync();
            }).Should().ThrowAsync<TrueException>().WithMessage("*OnNext(100)*");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectErrorAsync()
        {
            var ex = await Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(1)
                .ExpectErrorAsync();
            ex.Should().Be(Ex());
        }

        [Fact]
        public async Task TestSink_Probe_ExpectErrorAsync_fail_if_no_error_signalled()
        {
            await Awaiting(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(1)
                    .ExpectErrorAsync();
            }).Should().ThrowAsync<TrueException>().WithMessage("*OnNext(1)*");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectCompleteAsync_should_fail_if_error_signalled()
        {
            await Awaiting(async () =>
            {
                await Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }).Should().ThrowAsync<TrueException>().WithMessage("*OnError(Boom!)*");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectCompleteAsync_should_fail_if_next_element_signalled()
        {
            await Awaiting(async () =>
            {
                await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }).Should().ThrowAsync<TrueException>().WithMessage("*OnNext(1)*");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrCompleteAsync_with_right_element()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(4)
                .ExpectNextOrComplete(1)
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextOrCompleteAsync_with_completion()
        {
            await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(4)
                .ExpectNextOrComplete(1)
                .ExpectNextOrComplete(1337)
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextAsync_should_pass_with_right_element()
        {
            var result = await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(1)
                .ExpectNextAsync<int>(i => i == 1);
            result.ShouldBe(1);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextPredicateAsync_should_fail_with_wrong_element()
        {
            await Awaiting(async () =>
            {
                await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(1)
                    .ExpectNextAsync<int>(i => i == 2);
            }).Should().ThrowAsync<TrueException>().WithMessage("Got a message of the expected type*");
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_pass_with_right_element()
        {
            await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(1)
                .MatchNext<int>(i => i == 1)
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_allow_to_chain_test_methods()
        {
            await Source.From(Enumerable.Range(1, 2)).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(2)
                .MatchNext<int>(i => i == 1)
                .ExpectNext(2)
                .ExecuteAsync();
        }

        [Fact]
        public async Task TestSink_Probe_MatchNextAsync_should_fail_with_wrong_element()
        {
            await Awaiting(async () =>
            {
                await Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(1)
                    .MatchNext<int>(i => i == 2)
                    .ExecuteAsync();
            }).Should().ThrowAsync<TrueException>().WithMessage("Got a message of the expected type*");
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextNAsync_given_a_number_of_elements()
        {
            var result = await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextNAsync(4)
                .ToListAsync();
            result.Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public async Task TestSink_Probe_ExpectNextNAsync_given_specific_elements()
        {
            await Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .AsyncBuilder()
                .Request(4)
                .ExpectNextN(new[] { 1, 2, 3, 4 })
                .ExecuteAsync();
        }
    }
}
