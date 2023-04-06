//-----------------------------------------------------------------------
// <copyright file="LazySourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Extensions;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using FluentAssertions.Extensions;
using Xunit.Sdk;

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
        public async Task A_lazy_source_must_work_like_a_normal_source_happy_path()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var result = Source.Lazily(() => Source.From(new[] { 1, 2, 3 })).RunWith(Sink.Seq<int>(), Materializer);
                var complete = await result.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(ImmutableList.Create(1, 2, 3));
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_work_never_construct_the_source_when_there_was_no_demand()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = this.CreateSubscriberProbe<int>();
                var constructed = new AtomicBoolean();
                Source.Lazily(() =>
                {
                    constructed.GetAndSet(true);
                    return Source.From(new[] { 1, 2, 3 });
                }).RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.Cancel();
                constructed.Value.Should().BeFalse();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_fail_the_materialized_value_when_downstream_cancels_without_ever_consuming_any_element()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var result = Source.Lazily(() => Source.From(new[] { 1, 2, 3 }))                                                                             
                .ToMaterialized(Sink.Cancelled<int>(), Keep.Left)                                                                             
                .Run(Materializer);

                AssertThrows<Exception>(() =>
                {
                    var boom = result.Result;
                });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_stop_consuming_when_downstream_has_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var outProbe = this.CreateSubscriberProbe<int>();
                var inProbe = this.CreatePublisherProbe<int>();

                Source.Lazily(() => Source.FromPublisher(inProbe)).RunWith(Sink.FromSubscriber(outProbe), Materializer);

                outProbe.Request(1);
                await inProbe.ExpectRequestAsync();
                await inProbe.SendNextAsync(27);
                await outProbe.ExpectNextAsync(27);
                await outProbe.CancelAsync();
                await inProbe.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_materialize_when_the_source_has_been_created()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = this.CreateSubscriberProbe<int>();

                var task = Source.Lazily(() => Source.From(new[] { 1, 2, 3 }).MapMaterializedValue(_ => Done.Instance))
                    .To(Sink.FromSubscriber(probe))
                    .Run(Materializer);

                task.IsCompleted.Should().BeFalse();
                probe.Request(1);
                await probe.ExpectNextAsync(1);
                task.Result.Should().Be(Done.Instance);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_propagate_downstream_cancellation_cause_when_inner_source_has_been_materialized()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = CreateTestProbe();
                var (doneF, killSwitch) = Source.Lazily(() =>
                    {
                        return Source
                            .Maybe<int>()
                            .WatchTermination(Keep.Right)
                            .MapMaterializedValue(done =>
                            {
                                probe.Ref.Tell(Done.Instance, Nobody.Instance);
                                return done;
                            });
                    })
                    .MapMaterializedValue(t => t.Unwrap())
                    .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                    .To(Sink.Ignore<int>())
                    .Run(Materializer);

                var boom = new TestException("boom");
                await probe.ExpectMsgAsync<Done>();
                killSwitch.Abort(boom);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                doneF.ContinueWith(t =>
                {
                    t.Exception.Should().NotBeNull();
                    t.Exception.InnerException.Should().NotBeNull();
                    t.Exception.InnerException.Should().Be(boom);
                });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }, Materializer);
        }
        
        [Fact]
        public async Task A_lazy_source_must_fail_stage_when_upstream_fails()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var outProbe = this.CreateSubscriberProbe<int>();
                var inProbe = this.CreatePublisherProbe<int>();

                Source.Lazily(() => Source.FromPublisher(inProbe)).RunWith(Sink.FromSubscriber(outProbe), Materializer);

                outProbe.Request(1);
                await inProbe.ExpectRequestAsync();
                await inProbe.SendNextAsync(27);
                await outProbe.ExpectNextAsync(27);

                var testException = new TestException("OMG Who set that on fire !?!");
                inProbe.SendError(testException);
                outProbe.ExpectError().Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_propagate_attributes_to_inner_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var attributesSource = Source.FromGraph(new AttibutesSourceStage())
                    .AddAttributes(Attributes.CreateName("inner"));

                var first = Source.Lazily(() => attributesSource)
                    .AddAttributes(Attributes.CreateName("outer"))
                    .RunWith(Sink.First<Attributes>(), Materializer);

                var complete = await first.ShouldCompleteWithin(3.Seconds());
                var attributes = complete.AttributeList.ToList();
                var inner = new Attributes.Name("inner");
                var outer = new Attributes.Name("outer");
                attributes.Should().Contain(inner);
                attributes.Should().Contain(outer);
                attributes.IndexOf(outer).Should().BeLessThan(attributes.IndexOf(inner));
            }, Materializer);
        }

        [Fact]
        public async Task A_lazy_source_must_fail_correctly_when_materialization_of_inner_source_fails()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                var matFail = new TestException("fail!");

                var task = Source.Lazily(() => Source.FromGraph(new FailingInnerMat(matFail)))
                    .To(Sink.Ignore<string>())
                    .Run(Materializer);

                try
                {
                    task.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException) { }

                task.IsFaulted.ShouldBe(true);
                task.Exception.ShouldNotBe(null);
                task.Exception.InnerException.Should().BeEquivalentTo(matFail);
                return Task.CompletedTask;
            }, Materializer);
        }

        private sealed class FailingInnerMat : GraphStage<SourceShape<string>>
        {
            #region Logic
            private sealed class FailingLogic : GraphStageLogic
            {
                public FailingLogic(Shape shape, TestException ex) : base(shape)
                {
                    throw ex;
                }
            }
            #endregion

            public FailingInnerMat(TestException ex)
            {
                var outlet = new Outlet<string>("out");
                Shape = new SourceShape<string>(outlet);
                _ex = ex;
            }

            private readonly TestException _ex;

            public override SourceShape<string> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new FailingLogic(Shape, _ex);
            }
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
