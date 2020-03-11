//-----------------------------------------------------------------------
// <copyright file="FlowOrElseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowOrElseSpec : AkkaSpec
    {
        private const char A = 'a';

        public FlowOrElseSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = Sys.Materializer();
        }

        private ActorMaterializer Materializer { get; }

        [Fact]
        public void An_OrElse_flow_should_pass_elements_from_the_first_input()
        {
            var source1 = Source.From(new[] {1, 2, 3});
            var source2 = Source.From(new[] {4, 5, 6});

            var sink = Sink.Seq<int>();

            source1.OrElse(source2).RunWith(sink, Materializer).AwaitResult().ShouldAllBeEquivalentTo(new[] {1, 2, 3});
        }

        [Fact]
        public void An_OrElse_flow_should_pass_elements_from_the_second_input_if_the_first_completes_with_no_elements_emitted()
        {
            var source1 = Source.Empty<int>();
            var source2 = Source.From(new[] { 4, 5, 6 });

            var sink = Sink.Seq<int>();

            source1.OrElse(source2).RunWith(sink, Materializer).AwaitResult().ShouldAllBeEquivalentTo(new[] { 4, 5, 6 });
        }

        [Fact]
        public void An_OrElse_flow_should_pass_elements_from_input_one_through_and_cancel_input_2()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.Request(1);
                inProbe1.ExpectRequest();
                inProbe1.SendNext(A);
                outProbe.ExpectNext(A);
                inProbe1.SendComplete();
                inProbe2.ExpectCancellation();
                outProbe.ExpectComplete();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_pass_elements_from_input_two_when_input_1_has_completed_without_elements()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.Request(1);
                inProbe1.SendComplete();
                inProbe2.ExpectRequest();
                inProbe2.SendNext(A);
                outProbe.ExpectNext(A);
                inProbe2.SendComplete();
                outProbe.ExpectComplete();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_pass_elements_from_input_two_when_input_1_has_completed_without_elements_LazyEmpty()
        {
            var inProbe1 = TestPublisher.LazyEmpty<char>();
            var source1 = Source.FromPublisher(inProbe1);
            var inProbe2 = this.CreatePublisherProbe<char>();
            var source2 = Source.FromPublisher(inProbe2);

            var outProbe = this.CreateSubscriberProbe<char>();
            var sink = Sink.FromSubscriber(outProbe);

            source1.OrElse(source2).RunWith(sink, Materializer);

            outProbe.Request(1);
            inProbe2.ExpectRequest();
            inProbe2.SendNext(A);
            outProbe.ExpectNext(A);
            inProbe2.SendComplete();

            outProbe.ExpectComplete();
        }

        [Fact]
        public void An_OrElse_flow_should_pass_all_available_requested_elements_from_input_two_when_input_1_has_completed_without_elements()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.Request(5);

                inProbe1.SendComplete();

                inProbe2.ExpectRequest();
                inProbe2.SendNext(A);
                outProbe.ExpectNext(A);

                inProbe2.SendNext('b');
                outProbe.ExpectNext('b');

                inProbe2.SendNext('c');
                outProbe.ExpectNext('c');

                inProbe2.SendComplete();
                outProbe.ExpectComplete();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_complete_when_both_inputs_completes_without_emitting_elements()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.EnsureSubscription();
                inProbe1.SendComplete();
                inProbe2.SendComplete();
                outProbe.ExpectComplete();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_complete_when_both_inputs_completes_without_emitting_elements_regardless_of_order()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.EnsureSubscription();
                inProbe1.SendComplete();
                outProbe.ExpectNoMsg(); // make sure it did not complete here
                inProbe2.SendComplete();
                outProbe.ExpectComplete();

            });
        }

        [Fact]
        public void An_OrElse_flow_should_continue_passing_primary_through_when_secondary_completes()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.EnsureSubscription();
                outProbe.Request(1);
                inProbe2.SendComplete();

                inProbe1.ExpectRequest();
                inProbe1.SendNext(A);
                outProbe.ExpectNext(A);

                inProbe1.SendComplete();
                outProbe.ExpectComplete();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_fail_when_input_1_fails()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.EnsureSubscription();
                inProbe1.SendError(new TestException("in1 failed"));
                inProbe2.ExpectCancellation();
                outProbe.ExpectError();
            });
        }

        [Fact]
        public void An_OrElse_flow_should_fail_when_input_2_fails()
        {
            OrElseProbedFlow((inProbe1, inProbe2, outProbe) =>
            {
                outProbe.EnsureSubscription();
                inProbe2.SendError(new TestException("in1 failed"));
                inProbe1.ExpectCancellation();
                outProbe.ExpectError();
            });
        }

        private void OrElseProbedFlow(Action<TestPublisher.Probe<char>, TestPublisher.Probe<char>, TestSubscriber.Probe<char>> assert)
        {
            var inProbe1 = this.CreatePublisherProbe<char>();
            var source1 = Source.FromPublisher(inProbe1);
            var inProbe2 = this.CreatePublisherProbe<char>();
            var source2 = Source.FromPublisher(inProbe2);

            var outProbe = this.CreateSubscriberProbe<char>();
            var sink = Sink.FromSubscriber(outProbe);

            source1.OrElse(source2).RunWith(sink, Materializer);

            assert(inProbe1, inProbe2, outProbe);
        }
    }
}
