//-----------------------------------------------------------------------
// <copyright file="StreamTestKitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
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
        public void TestSink_Probe_ToStrict()
        {
            Source.From(Enumerable.Range(1, 4))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .ToStrict(TimeSpan.FromMilliseconds(300))
                .Should()
                .Equal(1, 2, 3, 4);
        }

        [Fact]
        public void TestSink_Probe_ToStrict_with_failing_source()
        {
            var error = Record.Exception(() =>
            {
                Source.From(Enumerable.Range(1, 3).Select(i =>
                {
                    if (i == 3)
                        throw Ex();
                    return i;
                })).RunWith(this.SinkProbe<int>(), Materializer)
                    .ToStrict(TimeSpan.FromMilliseconds(300));
            });

            var aggregateException = error.InnerException;
            aggregateException.InnerException.Message.Should().Contain("Boom!");
            error.Message.Should().Contain("1, 2");
        }

        [Fact]
        public void TestSink_Probe_ToStrict_when_subscription_was_already_obtained()
        {
            var p = Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer);
            p.ExpectSubscription();
            p.ToStrict(TimeSpan.FromMilliseconds(300)).Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public void TestSink_Probe_ExpectNextOrError_with_right_element()
        {
            Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrError(1, Ex());
        }

        [Fact]
        public void TestSink_Probe_ExpectNextOrError_with_right_exception()
        {
            Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrError(1, Ex());
        }

        [Fact]
        public void TestSink_Probe_ExpectNextOrError_fail_if_the_next_element_is_not_the_expected_one()
        {
            Record.Exception(() =>
            {
                Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(4)
                    .ExpectNextOrError(100, Ex());
            }).Message.Should().Contain("OnNext(100)");
        }

        [Fact]
        public void TestSink_Probe_ExpectError()
        {
            Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .ExpectError().Should().Be(Ex());
        }

        [Fact]
        public void TestSink_Probe_ExpectError_fail_if_no_error_signalled()
        {
            Record.Exception(() =>
            {
                Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectError();
            }).Message.Should().Contain("OnNext");
        }

        [Fact]
        public void TestSink_Probe_ExpectComplete_should_fail_if_error_signalled()
        {
            Record.Exception(() =>
            {
                Source.Failed<int>(Ex()).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }).Message.Should().Contain("OnError");
        }

        [Fact]
        public void TestSink_Probe_ExpectComplete_should_fail_if_next_element_signalled()
        {
            Record.Exception(() =>
            {
                Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }).Message.Should().Contain("OnNext");
        }

        [Fact]
        public void TestSink_Probe_ExpectNextOrComplete_with_right_element()
        {
            Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrComplete(1);
        }

        [Fact]
        public void TestSink_Probe_ExpectNextOrComplete_with_completion()
        {
            Source.Single(1).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextOrComplete(1)
                .ExpectNextOrComplete(1337);
        }

        [Fact]
        public void TestSink_Probe_ExpectNextPredicate_should_pass_with_right_element()
        {
            Source.Single(1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .ExpectNext<int>(i => i == 1)
                .ShouldBe(1);
        }

        [Fact]
        public void TestSink_Probe_ExpectNextPredicate_should_fail_with_wrong_element()
        {
            Record.Exception(() =>
            {
                Source.Single(1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectNext<int>(i => i == 2);
            }).Message.ShouldStartWith("Got a message of the expected type");
        }

        [Fact]
        public void TestSink_Probe_MatchNext_should_pass_with_right_element()
        {
            Source.Single(1)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .MatchNext<int>(i => i == 1);
        }

        [Fact]
        public void TestSink_Probe_MatchNext_should_allow_to_chain_test_methods()
        {
            Source.From(Enumerable.Range(1, 2))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(2)
                .MatchNext<int>(i => i == 1)
                .ExpectNext(2);
        }

        [Fact]
        public void TestSink_Probe_MatchNext_should_fail_with_wrong_element()
        {
            Record.Exception(() =>
            {
                Source.Single(1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .MatchNext<int>(i => i == 2);
            }).Message.ShouldStartWith("Got a message of the expected type");
        }

        [Fact]
        public void TestSink_Probe_ExpectNextN_given_a_number_of_elements()
        {
            Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextN(4).Should().Equal(1, 2, 3, 4);
        }

        [Fact]
        public void TestSink_Probe_ExpectNextN_given_specific_elements()
        {
            Source.From(Enumerable.Range(1, 4)).RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNextN(new[] {1, 2, 3, 4});
        }
    }
}
