using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowTakeWhileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowTakeWhileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_TakeWhile_must_take_while_predicate_is_true()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 4))
                    .TakeWhile(i => i < 3)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectNext(1,2)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeWhile_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .TakeWhile(i => i < 2)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_TakeWhile_must_continue_if_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new Exception("test");

                Source.From(Enumerable.Range(1, 4)).TakeWhile(a =>
                {
                    if (a == 3)
                        throw testException;
                    return true;
                })
                    .WithAttributes(new Attributes(new ActorAttributes.SupervisionStrategy(Deciders.ResumingDecider)))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(4)
                    .ExpectNext(1, 2, 4)
                    .ExpectComplete();
            }, Materializer);
        }
    }
}
