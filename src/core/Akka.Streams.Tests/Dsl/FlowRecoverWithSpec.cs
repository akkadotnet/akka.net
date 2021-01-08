//-----------------------------------------------------------------------
// <copyright file="FlowRecoverWithSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowRecoverWithSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowRecoverWithSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static readonly TestException Ex = new TestException("test");

        [Fact]
        public void A_RecoverWith_must_recover_when_there_is_a_handler()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 4)).Select(x =>
                {
                    if (x == 3)
                        throw Ex;
                    return x;
                }).RecoverWithRetries(_ => Source.From(new[] {0, -1}), -1).RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(2)
                    .ExpectNext(1)
                    .ExpectNext(2);

                probe
                    .Request(1)
                    .ExpectNext(0);

                probe
                    .Request(1)
                    .ExpectNext(-1)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_cancel_substream_if_parent_is_terminated_when_there_is_a_handler()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 4)).Select(x =>
                {
                    if (x == 3)
                        throw Ex;
                    return x;
                }).RecoverWithRetries(_ => Source.From(new[] {0, -1}), -1).RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe
                    .Request(1)
                    .ExpectNext(0);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_failed_stream_if_handler_is_not_for_such_exception_type()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 3)).Select(x =>
                {
                    if (x == 2)
                        throw Ex;
                    return x;
                }).RecoverWithRetries(_ => null, -1).RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(1)
                    .ExpectNext(1);

                probe
                    .Request(1)
                    .ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_be_able_to_recover_with_the_same_unmaterialized_source_if_configured()
        {
            this.AssertAllStagesStopped(() =>
            {
                var src = Source.From(Enumerable.Range(1, 3)).Select(x =>
                {
                    if (x == 3)
                        throw Ex;
                    return x;
                });
                var probe = src.RecoverWithRetries(_ => src, -1).RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_not_influence_stream_when_there_is_no_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Select(x => x)
                    .RecoverWithRetries(_ => Source.Single(0), -1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectNext(1, 2, 3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_finish_stream_if_it_is_empty()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Select(x => x)
                    .RecoverWithRetries(_ => Source.Single(0), -1)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_switch_the_second_time_if_alternative_source_throws_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 3)).Select(x =>
                {
                    if (x == 3)
                        throw new IndexOutOfRangeException();
                    return x;
                }).RecoverWithRetries(ex =>
                {
                    if (ex is IndexOutOfRangeException)
                        return Source.From(new [] {11,22}).Select(x =>
                        {
                            if (x == 22)
                                throw new ArgumentException();
                            return x;
                        });
                    if (ex is ArgumentException)
                        return Source.From(new[] { 33, 44 });
                    return null;
                }, -1).RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe
                    .Request(2)
                    .ExpectNext(11, 33);

                probe
                    .Request(1)
                    .ExpectNext(44)
                    .ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void A_RecoverWith_must_terminate_with_exception_if_partial_function_fails_to_match_after_an_alternative_source_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 3))
                    .Select(x =>
                    {
                        if(x==3)
                            throw new IndexOutOfRangeException();
                        return x;
                    })
                    .RecoverWithRetries(ex =>
                    {
                        if (ex is IndexOutOfRangeException)
                            return Source.From(new[] {11, 22}).Select(x =>
                            {
                                if (x == 22)
                                    throw Ex;
                                return x;
                            });
                        return null;
                    }, -1)
                    .RunWith(this.SinkProbe<int>(), Materializer);

                probe
                    .Request(2)
                    .ExpectNext(1, 2);

                probe
                    .Request(1)
                    .ExpectNext(11);

                probe
                    .Request(1)
                    .ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_terminate_with_exception_after_set_number_of_retries()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.From(Enumerable.Range(1, 3))
                    .Select(x =>
                    {
                        if (x == 3)
                            throw new IndexOutOfRangeException();
                        return x;
                    })
                    .RecoverWithRetries(_ => Source.From(new[] {11, 22, 33})
                        .Select(x =>
                        {
                            if (x == 33)
                                throw Ex;
                            return x;
                        }), 3)
                    .RunWith(this.SinkProbe<int>(), Materializer);

                probe.Request(2).ExpectNext(1, 2);
                probe.Request(2).ExpectNext(11, 22);
                probe.Request(2).ExpectNext(11, 22);
                probe.Request(2).ExpectNext(11, 22);
                probe.Request(1).ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_throw_ArgumentException_if_number_of_retries_is_less_than_minus_one()
        {
            this.AssertAllStagesStopped(() =>
            {
                Flow.Create<int>()
                    .Invoking(f => f.RecoverWithRetries(exception => Source.Empty<int>(), -2))
                    .ShouldThrow<ArgumentException>();
            }, Materializer);
        }

        [Fact]
        public void A_RecoverWith_must_fail_correctly_when_materialization_of_recover_source_fails()
        {
            this.AssertAllStagesStopped(() => 
            {
                var matFail = new TestException("fail!");

                var task = Source.Failed<string>(new TestException("trigger"))
                    .RecoverWithRetries(ex => Source.FromGraph(new FailingInnerMat(matFail)), 1)
                    .RunWith(Sink.Ignore<string>(), Materializer);

                try
                {
                    task.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException) { }

                task.IsFaulted.ShouldBe(true);
                task.Exception.ShouldNotBe(null);
                task.Exception.InnerException.ShouldBeEquivalentTo(matFail);

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

    }
}
