//-----------------------------------------------------------------------
// <copyright file="FlowTakeUntilSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowTakeUntilSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowTakeUntilSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_TakeUntil_must_take_while_predicate_is_true()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 4))
                    .TakeUntil(i => i == 3)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectNext(1, 2)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeUntil_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .TakeUntil(i => i < 2)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeUntil_must_continue_if_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new Exception("test");

                Source.From(Enumerable.Range(1, 4)).TakeUntil(a =>
                {
                    if (a == 3)
                        throw testException;
                    return false;
                })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(4)
                    .ExpectNext(1, 2, 4)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeUntil_must_emit_the_element_that_caused_the_predicate_to_return_false_and_then_no_more_with_inclusive_set()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 10))
                .TakeUntil(i => i == 3, true)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(4)
                .ExpectNext(1, 2, 3)
                .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeUntil_must_not_request_upstream_after_predicate_is_true()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (source, sink) = this.SourceProbe<int>()
                    .TakeUntil(i => i == 3)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                sink.Request(3);
                source.SendNext(1).SendNext(2).SendNext(3).ExpectCancellation();
                sink.ExpectNext(1, 2).ExpectComplete();

            }, Materializer);
        }
    }
}
