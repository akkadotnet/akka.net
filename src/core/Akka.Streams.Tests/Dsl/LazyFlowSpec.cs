//-----------------------------------------------------------------------
// <copyright file="LazyFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
        public void A_LazyFlow_must_work_in_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                Func<Task<Flow<int, string, NotUsed>>> MapF(int e) => () =>
                    Task.FromResult(Flow.FromFunction<int, string>(i => (i * e).ToString()));

                var probe = Source.From(Enumerable.Range(2, 10))
                    .Via(Flow.LazyInitAsync(MapF(2)))
                    .RunWith(this.SinkProbe<string>(), Materializer);
                probe.Request(100);
                Enumerable.Range(2, 10).Select(i => (i * 2).ToString()).ForEach(i => probe.ExpectNext(i));
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_work_with_slow_flow_init()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = new TaskCompletionSource<Flow<int, int, NotUsed>>();
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var flowProbe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => p.Task))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                flowProbe.Request(1);
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectRequest(1);
                sourceProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

                p.SetResult(Flow.Create<int>());
                flowProbe.Request(99);
                flowProbe.ExpectNext(0);
                Enumerable.Range(1, 10).ForEach(i =>
                 {
                     sourceSub.SendNext(i);
                     flowProbe.ExpectNext(i);
                 });
                sourceSub.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_complete_when_there_was_no_elements_in_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.Empty<int>()
                    .Via(Flow.LazyInitAsync(() => FlowF))
                    .RunWith(this.SinkProbe<int>(), Materializer);
                probe.Request(1).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_complete_normally_when_upstream_completes_BEFORE_the_stage_has_switched_to_the_inner_flow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var promise = new TaskCompletionSource<Flow<int, int, NotUsed>>();
                var (pub, sub) = this.SourceProbe<int>()
                    .ViaMaterialized(Flow.LazyInitAsync(() => promise.Task), Keep.Left)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                sub.Request(1);
                pub.SendNext(1).SendComplete();
                promise.SetResult(Flow.Create<int>());
                sub.ExpectNext(1).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_complete_normally_when_upstream_completes_AFTER_the_stage_has_switched_to_the_inner_flow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (pub, sub) = this.SourceProbe<int>()
                    .ViaMaterialized(Flow.LazyInitAsync(() => Task.FromResult(Flow.Create<int>())), Keep.Left)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                sub.Request(1);
                pub.SendNext(1);
                sub.ExpectNext(1);
                pub.SendComplete();
                sub.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_fail_gracefully_when_flow_factory_method_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync<int, int, NotUsed>(() => throw Ex))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                probe.Request(1);
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectCancellation();
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_fail_gracefully_when_upstream_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => FlowF))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                probe.Request(1).ExpectNext(0);
                sourceSub.SendError(Ex);
                probe.ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_fail_gracefully_when_factory_task_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var flowprobe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => Task.FromException<Flow<int, int, NotUsed>>(Ex)))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                var error = flowprobe.Request(1).ExpectError().As<AggregateException>();
                error.Flatten().InnerException.Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_cancel_upstream_when_the_downstream_is_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = this.CreateManualPublisherProbe<int>();
                var probe = Source.FromPublisher(sourceProbe)
                    .Via(Flow.LazyInitAsync(() => FlowF))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                var sourceSub = sourceProbe.ExpectSubscription();
                probe.Request(1);
                sourceSub.ExpectRequest(1);
                sourceSub.SendNext(0);
                sourceSub.ExpectRequest(1);
                probe.ExpectNext(0);
                probe.Cancel();
                sourceSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_LazyFlow_must_fail_correctly_when_factory_throw_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                const string msg = "fail!";
                var matFail = new TestException(msg);

                var result = Source.Single("whatever")
                    .ViaMaterialized(Flow.LazyInitAsync<string, string, Exception>(() => throw matFail), Keep.Right)
                    .ToMaterialized(Sink.Ignore<string>(), Keep.Left)
                    .Invoking(source => source.Run(Materializer));

                result.Should().Throw<TestException>().WithMessage(msg);
            }, Materializer);
        }
    }
}
