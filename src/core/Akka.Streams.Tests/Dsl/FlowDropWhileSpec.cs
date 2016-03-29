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
    public class FlowDropWhileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDropWhileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_DropWhile_must_drop_while_predicate_is_true()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 4))
                    .DropWhile(x => x < 3)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(2)
                    .ExpectNext(3, 4)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_DropWhile_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .DropWhile(x => x < 2)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_DropWhile_must_continue_if_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new Exception("test");
                Source.From(Enumerable.Range(1, 4)).DropWhile(x =>
                {
                    if (x < 3)
                        return true;
                    throw testException;
                })
                    .WithAttributes(new Attributes(new ActorAttributes.SupervisionStrategy(Deciders.ResumingDecider)))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }
    }
}
