//-----------------------------------------------------------------------
// <copyright file="LazySinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class LazySinkSpec : AkkaSpec
    {
        public LazySinkSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1,1);
            Materializer = Sys.Materializer(settings);
        }

        private ActorMaterializer Materializer { get; }

        private static Func<TMat> Fallback<TMat>()
        {
            return () =>
            {
                Assert.True(false, "Must not call fallback function");
                return default(TMat);
            };
        }

        private static readonly Exception Ex = new TestException("");

        [Fact]
        public void A_LazySink_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var lazySink = Sink.LazySink((int _) => Task.FromResult(this.SinkProbe<int>()),
                    Fallback<TestSubscriber.Probe<int>>());
                var taskProbe = Source.From(Enumerable.Range(0, 11)).RunWith(lazySink, Materializer);
                var probe = taskProbe.AwaitResult(RemainingOrDefault);
                probe.Request(100);
                Enumerable.Range(0, 11).ForEach(i => probe.ExpectNext(i));
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_work_with_slow_sink_init()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = new TaskCompletionSource<Sink<int, TestSubscriber.Probe<int>>>();
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var taskProbe = Source.FromPublisher(sourceProbe)
                        .RunWith(Sink.LazySink((int _) => p.Task, Fallback<TestSubscriber.Probe<int>>()), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectRequest(1);
                sourceProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                taskProbe.Wait(TimeSpan.FromMilliseconds(200)).ShouldBeFalse();

                p.SetResult(this.SinkProbe<int>());
                var probe = taskProbe.AwaitResult(RemainingOrDefault);
                probe.Request(100);
                probe.ExpectNext(0);
                Enumerable.Range(1,10).ForEach(i =>
                {
                    sourceSub.SendNext(i);
                    probe.ExpectNext(i);
                });
                sourceSub.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_complete_when_there_was_no_elements_in_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var lazySink = Sink.LazySink((int _) => Task.FromResult(Sink.Aggregate(0, (int i, int i2) => i + i2)),
                    () => Task.FromResult(0));
                var taskProbe = Source.Empty<int>().RunWith(lazySink, Materializer);
                var taskResult = taskProbe.AwaitResult(RemainingOrDefault);
                taskResult.AwaitResult(RemainingOrDefault).ShouldBe(0);
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_complete_normally_when_upstream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var lazySink = Sink.LazySink((int _) => Task.FromResult(this.SinkProbe<int>()),
                    Fallback<TestSubscriber.Probe<int>>());
                var taskProbe = Source.Single(1).RunWith(lazySink, Materializer);
                var taskResult = taskProbe.AwaitResult(RemainingOrDefault);
                taskResult.Request(1).ExpectNext(1).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_fail_gracefully_when_sink_factory_method_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(Sink.LazySink((int _) =>
                {
                    throw Ex;
                }, Fallback<TestSubscriber.Probe<int>>()), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectCancellation();
                taskProbe.Invoking(t => t.Wait()).ShouldThrow<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_fail_gracefully_when_upstream_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazySink((int _) => Task.FromResult(this.SinkProbe<int>()),
                    Fallback<TestSubscriber.Probe<int>>());
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(lazySink, Materializer);
                
                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                var probe = taskProbe.AwaitResult(RemainingOrDefault);
                probe.Request(1).ExpectNext(0);
                sourceSub.SendError(Ex);
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_fail_gracefully_when_factory_task_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var failedTask = new TaskFactory<Sink<int, TestSubscriber.Probe<int>>>().StartNew(() =>
                {
                    throw Ex;
                });
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazySink((int _) => failedTask, Fallback<TestSubscriber.Probe<int>>());
                var taskProbe =
                    Source.FromPublisher(sourceProbe)
                        .ToMaterialized(lazySink, Keep.Right)
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider))
                        .Run(Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                taskProbe.Invoking(t => t.Wait(TimeSpan.FromMilliseconds(300))).ShouldThrow<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_cancel_upstream_when_internal_sink_is_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazySink((int _) => Task.FromResult(this.SinkProbe<int>()),
                    Fallback<TestSubscriber.Probe<int>>());
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(lazySink, Materializer);
                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectRequest(1);
                var probe = taskProbe.AwaitResult(RemainingOrDefault);
                probe.Request(1).ExpectNext(0);
                probe.Cancel();
                sourceSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_continue_if_supervision_is_resume()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazySink((int a) =>
                    {
                        if (a == 0)
                            throw Ex;
                        return Task.FromResult(this.SinkProbe<int>());
                    },
                   Fallback<TestSubscriber.Probe<int>>());
                var taskProbe =
                    Source.FromPublisher(sourceProbe)
                        .ToMaterialized(lazySink, Keep.Right)
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                        .Run(Materializer);
                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(1);
                var probe = taskProbe.AwaitResult(RemainingOrDefault);
                probe.Request(1);
                probe.ExpectNext(1);
                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_fail_task_when_zero_throws_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var lazySink = Sink.LazySink((int _) => Task.FromResult(Sink.Aggregate<int, int>(0, (i, i1) => i + i1)),
                    () =>
                    {
                        throw Ex;
                    });
                var taskProbe = Source.Empty<int>().RunWith(lazySink, Materializer);
                taskProbe.Invoking(t => t.Wait(TimeSpan.FromMilliseconds(300))).ShouldThrow<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_LazySink_must_fail_correctly_when_materialization_of_inner_sink_fails()
        {
            this.AssertAllStagesStopped(() => 
            {
                var matFail = new TestException("fail!");

                var task = Source.Single("whatever")
                    .RunWith(Sink.LazySink<string, NotUsed>(
                        str => Task.FromResult(Sink.FromGraph(new FailingInnerMat(matFail))),
                        () => NotUsed.Instance), Materializer);

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

        private sealed class FailingInnerMat : GraphStage<SinkShape<string>>
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
                var inlet = new Inlet<string>("in");
                Shape = new SinkShape<string>(inlet);
                _ex = ex;
            }

            private readonly TestException _ex;

            public override SinkShape<string> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new FailingLogic(Shape, _ex);
            }
        }

    }
}
