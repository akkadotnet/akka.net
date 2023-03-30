//-----------------------------------------------------------------------
// <copyright file="LazySinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Akka.TestKit.Extensions;
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
        public async Task A_LazySink_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var lazySink = Sink.LazyInitAsync(() => Task.FromResult(this.SinkProbe<int>()));
                var taskProbe = Source.From(Enumerable.Range(0, 11)).RunWith(lazySink, Materializer);
                var probe = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                probe.Value.Request(100);
                foreach (var i in Enumerable.Range(0, 11)) 
                {
                    await probe.Value.ExpectNextAsync(i);
                }
                //I CHECK IT THIS IS NOT GOOD - EVERYTHING WORKS FINE NOW
               // Enumerable.Range(0, 11).ForEach(i => probe.Value.ExpectNext(i));
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_work_with_slow_sink_init()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var p = new TaskCompletionSource<Sink<int, TestSubscriber.Probe<int>>>();
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var taskProbe = Source.FromPublisher(sourceProbe)
                        .RunWith(Sink.LazyInitAsync(() => p.Task), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectRequestAsync(1);
                await sourceProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                taskProbe.Wait(TimeSpan.FromMilliseconds(200)).ShouldBeFalse();

                p.SetResult(this.SinkProbe<int>());
                var complete = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                var probe = complete.Value;
                probe.Request(100);
                await probe.ExpectNextAsync(0);

                foreach (var i in Enumerable.Range(1, 10))
                {
                    sourceSub.SendNext(i);
                    await probe.ExpectNextAsync(i);
                }
                //I CHECK IT THIS IS NOT GOOD - EVERYTHING WORKS FINE NOW
                /*
                 Enumerable.Range(1,10).ForEach(i =>
                {
                    sourceSub.SendNext(i);
                    probe.ExpectNext(i);
                });
                 */
                sourceSub.SendComplete();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_complete_when_there_was_no_elements_in_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var lazySink = Sink.LazyInitAsync(() => Task.FromResult(Sink.Aggregate(0, (int i, int i2) => i + i2)));
                var taskProbe = Source.Empty<int>().RunWith(lazySink, Materializer);
                var complete = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                complete.ShouldBe(Option<Task<int>>.None);
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_complete_normally_when_upstream_is_completed()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var lazySink = Sink.LazyInitAsync(() => Task.FromResult(this.SinkProbe<int>()));
                var taskProbe = Source.Single(1).RunWith(lazySink, Materializer);
                var taskResult = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                await taskResult.Value.Request(1).ExpectNext(1).ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_fail_gracefully_when_sink_factory_method_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(Sink.LazyInitAsync<int, NotUsed>(() => throw Ex), Materializer);
                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectCancellationAsync();
                taskProbe.Invoking(t => t.Wait()).Should().Throw<TestException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_fail_gracefully_when_upstream_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazyInitAsync(() => Task.FromResult(this.SinkProbe<int>()));
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(lazySink, Materializer);
                
                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                var complete = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                var probe = complete.Value;
                await probe.Request(1).ExpectNextAsync(0);
                sourceSub.SendError(Ex);
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_fail_gracefully_when_factory_task_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazyInitAsync(() => Task.FromException<Sink<int, TestSubscriber.Probe<int>>>(Ex));
                var taskProbe =
                    Source.FromPublisher(sourceProbe)
                        .ToMaterialized(lazySink, Keep.Right)
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider))
                        .Run(Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                taskProbe.Invoking(t => t.Wait(TimeSpan.FromMilliseconds(300))).Should().Throw<TestException>();
                
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_cancel_upstream_when_internal_sink_is_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var lazySink = Sink.LazyInitAsync(() => Task.FromResult(this.SinkProbe<int>()));
                var taskProbe = Source.FromPublisher(sourceProbe).RunWith(lazySink, Materializer);
                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectRequestAsync(1);
                var complete = await taskProbe.ShouldCompleteWithin(RemainingOrDefault);
                var probe = complete.Value;
                await probe.Request(1).ExpectNextAsync(0);
                probe.Cancel();
                await sourceSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazySink_must_fail_correctly_when_materialization_of_inner_sink_fails()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var matFail = new TestException("fail!");

                var task = Source.Single("whatever")
                    .RunWith(Sink.LazyInitAsync(() => Task.FromResult(Sink.FromGraph(new FailingInnerMat(matFail)))), Materializer);

                try
                {
                    task.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException) { }

                task.IsFaulted.ShouldBe(true);
                task.Exception.ShouldNotBe(null);
                task.Exception.Flatten().InnerException.Should().BeEquivalentTo(matFail);
                return Task.CompletedTask;
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
