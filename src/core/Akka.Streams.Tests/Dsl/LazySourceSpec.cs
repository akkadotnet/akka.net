//-----------------------------------------------------------------------
// <copyright file="LazySourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class LazySourceSpec : AkkaSpec
    {
        public LazySourceSpec()
        {
            Materializer = Sys.Materializer();
        }

        private ActorMaterializer Materializer { get; }

        [Fact]
        public void A_lazy_source_must_work_like_a_normal_source_happy_path()
        {
            this.AssertAllStagesStopped(() =>
            {
                var result = Source.Lazily(() => Source.From(new[] { 1, 2, 3 })).RunWith(Sink.Seq<int>(), Materializer);
                result.AwaitResult().Should().BeEquivalentTo(ImmutableList.Create(1, 2, 3));
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_work_never_construct_the_source_when_there_was_no_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                var constructed = new AtomicBoolean();
                Source.Lazily(() =>
                {
                    constructed.GetAndSet(true);
                    return Source.From(new[] { 1, 2, 3 });
                }).RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.Cancel();
                constructed.Value.Should().BeFalse();
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_fail_the_materialized_value_when_downstream_cancels_without_ever_consuming_any_element()
        {
            this.AssertAllStagesStopped(() =>
            {
                var result = Source.Lazily(() => Source.From(new[] { 1, 2, 3 }))
                    .ToMaterialized(Sink.Cancelled<int>(), Keep.Left)
                    .Run(Materializer);

                Intercept(() =>
                {
                    var boom = result.Result;
                });
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_stop_consuming_when_downstream_has_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var outProbe = this.CreateSubscriberProbe<int>();
                var inProbe = this.CreatePublisherProbe<int>();

                Source.Lazily(() => Source.FromPublisher(inProbe)).RunWith(Sink.FromSubscriber(outProbe), Materializer);

                outProbe.Request(1);
                inProbe.ExpectRequest();
                inProbe.SendNext(27);
                outProbe.ExpectNext(27);
                outProbe.Cancel();
                inProbe.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_materialize_when_the_source_has_been_created()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateSubscriberProbe<int>();

                var task = Source.Lazily(() => Source.From(new[] { 1, 2, 3 }).MapMaterializedValue(_ => Done.Instance))
                    .To(Sink.FromSubscriber(probe))
                    .Run(Materializer);

                task.IsCompleted.Should().BeFalse();
                probe.Request(1);
                probe.ExpectNext(1);
                task.Result.Should().Be(Done.Instance);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_fail_stage_when_upstream_fails()
        {
            this.AssertAllStagesStopped(() =>
            {
                var outProbe = this.CreateSubscriberProbe<int>();
                var inProbe = this.CreatePublisherProbe<int>();

                Source.Lazily(() => Source.FromPublisher(inProbe)).RunWith(Sink.FromSubscriber(outProbe), Materializer);

                outProbe.Request(1);
                inProbe.ExpectRequest();
                inProbe.SendNext(27);
                outProbe.ExpectNext(27);

                var testException = new TestException("OMG Who set that on fire !?!");
                inProbe.SendError(testException);
                outProbe.ExpectError().Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public void A_lazy_source_must_propagate_attributes_to_inner_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var attributesSource = Source.FromGraph(new AttibutesSourceStage())
                    .AddAttributes(Attributes.CreateName("inner"));

                var first = Source.Lazily(() => attributesSource)
                    .AddAttributes(Attributes.CreateName("outer"))
                    .RunWith(Sink.First<Attributes>(), Materializer);

                var attributes = first.AwaitResult().AttributeList.ToList();
                var inner = new Attributes.Name("inner");
                var outer = new Attributes.Name("outer");
                attributes.Should().Contain(inner);
                attributes.Should().Contain(outer);
                attributes.IndexOf(outer).Should().BeLessThan(attributes.IndexOf(inner));
            }, Materializer);
        }

        private sealed class AttibutesSourceStage : GraphStage<SourceShape<Attributes>>
        {
            #region Logic

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly AttibutesSourceStage _stage;
                private readonly Attributes _inheritedAttributes;

                public Logic(AttibutesSourceStage stage, Attributes inheritedAttributes) : base(stage.Shape)
                {
                    _stage = stage;
                    _inheritedAttributes = inheritedAttributes;

                    SetHandler(stage.Out, this);
                }

                public override void OnPull()
                {
                    Push(_stage.Out, _inheritedAttributes);
                    CompleteStage();
                }
            }

            #endregion


            public AttibutesSourceStage()
            {
                Shape = new SourceShape<Attributes>(Out);
            }

            private Outlet<Attributes> Out { get; } = new Outlet<Attributes>("AttributesSource.out");

            public override SourceShape<Attributes> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
        }
    }
}
