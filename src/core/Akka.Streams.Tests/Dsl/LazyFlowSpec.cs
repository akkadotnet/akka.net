//-----------------------------------------------------------------------
// <copyright file="LazyFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class LazyFlowSpec : AkkaSpec
    {
        public LazyFlowSpec(ITestOutputHelper helper)
            : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = Sys.Materializer(settings);
        }

        private ActorMaterializer Materializer { get; }

        private static readonly Func<NotUsed> Fallback = () => NotUsed.Instance;

        private static readonly Exception Ex = new TestException("");

        private static readonly Task<Flow<int, int, NotUsed>> FlowF = Task.FromResult(Flow.Create<int>());
        
        [Fact]
        public async Task A_LazyFlow_must_work_in_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                Func<Task<Flow<int, string, NotUsed>>> MapF(int e) => () =>                                                                             
                Task.FromResult(Flow.FromFunction<int, string>(i => (i * e).ToString()));

                var probe = Source.From(Enumerable.Range(2, 10))
                    .Via(Flow.LazyInitAsync(MapF(2)))
                    .RunWith(this.SinkProbe<string>(), Materializer);
                probe.Request(100);
                foreach(var i in Enumerable.Range(2, 10).Select(i => (i * 2).ToString()))
                {
                    await probe.ExpectNextAsync(i);
                }                
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_work_with_slow_flow_init()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = new TaskCompletionSource<Flow<int, int, NotUsed>>();
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var flowProbe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => p.Task))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                flowProbe.Request(1);
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectRequestAsync(1);
                await sourceProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                p.SetResult(Flow.Create<int>());
                flowProbe.Request(99);
                await flowProbe.ExpectNextAsync(0);
                foreach(var i in Enumerable.Range(0, 10))
                {
                     sourceSub.SendNext(i);
                     await flowProbe.ExpectNextAsync(i);
                }
                sourceSub.SendComplete();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_complete_when_there_was_no_elements_in_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = Source.Empty<int>()                                                                             
                .Via(Flow.LazyInitAsync(() => FlowF))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer);
                await probe.Request(1).ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_complete_normally_when_upstream_completes_BEFORE_the_stage_has_switched_to_the_inner_flow()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var promise = new TaskCompletionSource<Flow<int, int, NotUsed>>();
                var (pub, sub) = this.SourceProbe<int>()
                    .ViaMaterialized(Flow.LazyInitAsync(() => promise.Task), Keep.Left)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                sub.Request(1);
                await pub.SendNext(1).SendCompleteAsync();
                promise.SetResult(Flow.Create<int>());
                await sub.ExpectNext(1).ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_complete_normally_when_upstream_completes_AFTER_the_stage_has_switched_to_the_inner_flow()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var (pub, sub) = this.SourceProbe<int>()                                                                             
                .ViaMaterialized(Flow.LazyInitAsync(() => Task.FromResult(Flow.Create<int>())), Keep.Left)                                                                             
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                             
                .Run(Materializer);

                sub.Request(1);
                await pub.SendNextAsync(1);
                await sub.ExpectNextAsync(1);
                await pub.SendCompleteAsync();
                await sub.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_fail_gracefully_when_flow_factory_method_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync<int, int, NotUsed>(() => throw Ex))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                probe.Request(1);
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectCancellationAsync();
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_fail_gracefully_when_upstream_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => FlowF))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await probe.Request(1).ExpectNextAsync(0);
                sourceSub.SendError(Ex);
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_fail_gracefully_when_factory_task_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var flowprobe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => Task.FromException<Flow<int, int, NotUsed>>(Ex)))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                var error = flowprobe.Request(1).ExpectError().As<AggregateException>();
                error.Flatten().InnerException.Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_cancel_upstream_when_the_downstream_is_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => FlowF))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = await sourceProbe.ExpectSubscriptionAsync();
                probe.Request(1);
                await sourceSub.ExpectRequestAsync(1);
                sourceSub.SendNext(0);
                await sourceSub.ExpectRequestAsync(1);
                await probe.ExpectNextAsync(0);
                probe.Cancel();
                await sourceSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_LazyFlow_must_fail_correctly_when_factory_throw_error()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                const string msg = "fail!";
                var matFail = new TestException(msg);

                var result = Source.Single("whatever")
                    .ViaMaterialized(Flow.LazyInitAsync<string, string, Exception>(() => throw matFail), Keep.Right)
                    .ToMaterialized(Sink.Ignore<string>(), Keep.Left)
                    .Invoking(source => source.Run(Materializer));

                result.Should().Throw<TestException>().WithMessage(msg);
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
