//-----------------------------------------------------------------------
// <copyright file="FlowKillSwitchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowKillSwitchSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowKillSwitchSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        #region unique kill switch

        [Fact]
        public async Task A_UniqueKillSwitch_must_stop_a_stream_if_requested()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var upstream = t.Item1.Item1;
            var killSwitch = t.Item1.Item2;
            var downstream = t.Item2;

            await downstream.RequestAsync(1);
            await upstream.SendNextAsync(1);
            await downstream.ExpectNextAsync(1);

            killSwitch.Shutdown();

            await upstream.ExpectCancellationAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_UniqueKillSwitch_must_fail_a_stream_if_requested()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var upstream = t.Item1.Item1;
            var killSwitch = t.Item1.Item2;
            var downstream = t.Item2;

            await downstream.RequestAsync(1);
            await upstream.SendNextAsync(1);
            await downstream.ExpectNextAsync(1);

            var testException = new TestException("Abort");
            killSwitch.Abort(testException);

            await upstream.ExpectCancellationAsync();
            //is a AggregateException from the Task
            downstream.ExpectError().InnerException.Should().Be(testException);
        }

        [Fact]
        public async Task A_UniqueKillSwitch_must_work_if_used_multiple_times_in_a_flow()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                //ex is a AggregateException from the Task
                .Recover(ex => ex.InnerException is TestException ? -1 : Option<int>.None)
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var upstream = t.Item1.Item1.Item1;
            var killSwitch1 = t.Item1.Item1.Item2;
            var killSwitch2 = t.Item1.Item2;
            var downstream = t.Item2;

            await downstream.RequestAsync(1);
            await upstream.SendNextAsync(1);
            await downstream.ExpectNextAsync(1);

            var testException = new TestException("Abort");
            killSwitch1.Abort(testException);
            await upstream.ExpectCancellationAsync();
            await downstream.RequestNextAsync(-1);

            killSwitch2.Shutdown();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_UniqueKillSwitch_must_ignore_completion_after_already_completed()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var upstream = t.Item1.Item1;
            var killSwitch = t.Item1.Item2;
            var downstream = t.Item2;

            await upstream.EnsureSubscriptionAsync();
            await downstream.EnsureSubscriptionAsync();

            killSwitch.Shutdown();
            await upstream.ExpectCancellationAsync();
            await downstream.ExpectCompleteAsync();

            killSwitch.Abort(new TestException("Won't happen"));
            await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        #endregion

        #region shared kill switch

        [Fact]
        public async Task A_SharedKillSwitch_must_stop_a_stream_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                killSwitch.Shutdown();
                await upstream.ExpectCancellationAsync();
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_fail_a_stream_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                var testException = new TestException("Abort");
                killSwitch.Abort(testException);
                await upstream.ExpectCancellationAsync();
                downstream.ExpectError().InnerException.Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_pass_through_all_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var killSwitch = KillSwitches.Shared("switch");
                var task = Source.From(Enumerable.Range(1, 100))
                    .Via(killSwitch.Flow<int>())
                    .RunWith(Sink.Seq<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_provide_a_flow_that_if_materialized_multiple_times_with_multiple_types_stops_all_streams_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t1 = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var t2 = this.SourceProbe<string>()
                    .Via(killSwitch.Flow<string>())
                    .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                    .Run(Materializer);

                var upstream1 = t1.Item1;
                var downstream1 = t1.Item2;
                var upstream2 = t2.Item1;
                var downstream2 = t2.Item2;

                await downstream1.RequestAsync(1);
                await upstream1.SendNextAsync(1);
                await downstream1.ExpectNextAsync(1);

                await downstream2.RequestAsync(2);
                await upstream2.SendNext("A").SendNextAsync("B");
                downstream2.ExpectNext("A", "B");

                killSwitch.Shutdown();

                await upstream1.ExpectCancellationAsync();
                await upstream2.ExpectCancellationAsync();
                await downstream1.ExpectCompleteAsync();
                await downstream2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_provide_a_flow_that_if_materialized_multiple_times_with_multiple_types_fails_all_streams_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t1 = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var t2 = this.SourceProbe<string>()
                    .Via(killSwitch.Flow<string>())
                    .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                    .Run(Materializer);

                var upstream1 = t1.Item1;
                var downstream1 = t1.Item2;
                var upstream2 = t2.Item1;
                var downstream2 = t2.Item2;

                await downstream1.RequestAsync(1);
                await upstream1.SendNextAsync(1);
                await downstream1.ExpectNextAsync(1);

                await downstream2.RequestAsync(2);
                await upstream2.SendNext("A").SendNextAsync("B");
                downstream2.ExpectNext("A", "B");

                var testException = new TestException("Abort");
                killSwitch.Abort(testException);
                await upstream1.ExpectCancellationAsync();
                await upstream2.ExpectCancellationAsync();

                downstream1.ExpectError().InnerException.Should().Be(testException);
                downstream2.ExpectError().InnerException.Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_ignore_subsequent_aborts_and_shutdowns_after_shutdown()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                killSwitch.Shutdown();
                await upstream.ExpectCancellationAsync();
                await downstream.ExpectCompleteAsync();

                killSwitch.Shutdown();
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                killSwitch.Abort(new TestException("Abort"));
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_ignore_subsequent_aborts_and_shutdowns_after_abort()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                var testException = new TestException("Abort");
                killSwitch.Abort(testException);
                await upstream.ExpectCancellationAsync();
                downstream.ExpectError().InnerException.Should().Be(testException);

                killSwitch.Shutdown();
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                killSwitch.Abort(new TestException("Abort_Late"));
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_complete_immediately_flows_materialized_after_switch_shutdown()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");
                killSwitch.Shutdown();

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await upstream.ExpectCancellationAsync();
                await downstream.ExpectSubscriptionAndCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_fail_immediately_flows_materialized_after_switch_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch = KillSwitches.Shared("switch");
                var testException = new TestException("Abort");
                killSwitch.Abort(testException);

                var t = this.SourceProbe<int>()
                    .Via(killSwitch.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await upstream.ExpectCancellationAsync();
                downstream.ExpectSubscriptionAndError().InnerException.Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_should_not_cause_problems_if_switch_is_shutdown_after_flow_completed_normally()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var killSwitch = KillSwitches.Shared("switch");
                var task = Source.From(Enumerable.Range(1, 10))
                    .Via(killSwitch.Flow<int>())
                    .RunWith(Sink.Seq<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                killSwitch.Shutdown();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_provide_flows_that_materialize_to_its_owner_KillSwitch()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var killSwitch = KillSwitches.Shared("switch");
                var t = Source.Maybe<int>()
                    .ViaMaterialized(killSwitch.Flow<int>(), Keep.Right)
                    .ToMaterialized(Sink.Ignore<int>(), Keep.Both)
                    .Run(Materializer);

                var killSwitch2 = t.Item1;
                var completion = t.Item2;
                killSwitch2.Should().Be(killSwitch);
                killSwitch2.Shutdown();
                completion.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                completion.IsFaulted.Should().BeFalse();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_not_affect_streams_corresponding_to_another_KillSwitch()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch1 = KillSwitches.Shared("switch");
                var killSwitch2 = KillSwitches.Shared("switch");

                var t1 = this.SourceProbe<int>()
                    .Via(killSwitch1.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var t2 = this.SourceProbe<int>()
                    .Via(killSwitch2.Flow<int>())
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                var upstream1 = t1.Item1;
                var downstream1 = t1.Item2;
                var upstream2 = t2.Item1;
                var downstream2 = t2.Item2;

                await downstream1.RequestAsync(1);
                await upstream1.SendNextAsync(1);
                await downstream1.ExpectNextAsync(1);

                await downstream2.RequestAsync(1);
                await upstream2.SendNextAsync(2);
                await downstream2.ExpectNextAsync(2);

                killSwitch1.Shutdown();
                await upstream1.ExpectCancellationAsync();
                await downstream1.ExpectCompleteAsync();
                await upstream2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                var testException = new TestException("Abort");
                killSwitch2.Abort(testException);
                await upstream1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await upstream2.ExpectCancellationAsync();
                downstream2.ExpectError().InnerException.Should().Be(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_allow_using_multiple_KillSwitch_in_one_graph()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var killSwitch1 = KillSwitches.Shared("switch");
                var killSwitch2 = KillSwitches.Shared("switch");

                var downstream = RunnableGraph.FromGraph(GraphDsl.Create(this.SinkProbe<int>(), (b, sink) =>
                {
                    var merge = b.Add(new Merge<int>(2));
                    var source1 = b.Add(Source.Maybe<int>().Via(killSwitch1.Flow<int>()));
                    var source2 = b.Add(Source.Maybe<int>().Via(killSwitch2.Flow<int>()));

                    b.From(source1).Via(merge).To(sink);
                    b.From(source2).To(merge);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                await downstream.EnsureSubscriptionAsync();
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                killSwitch1.Shutdown();
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                killSwitch2.Shutdown();
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SharedKillSwitch_must_use_its_name_on_the_flows_it_hands_out()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var killSwitch = KillSwitches.Shared("MySwitchName");
                killSwitch.ToString().Should().Be("KillSwitch(MySwitchName)");
                killSwitch.Flow<int>().ToString().Should().Be("Flow(KillSwitch(MySwitchName))");
                return Task.CompletedTask;
            }, Materializer);
        }
        
        #endregion

        #region cancellable kill switch

        [Fact]
        public async Task A_CancellationToken_flow_must_stop_a_stream_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();

                var t = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: true))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                cancel.Cancel();
                await upstream.ExpectCancellationAsync();
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_fail_a_stream_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();

                var t = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: false))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                cancel.Cancel();
                await upstream.ExpectCancellationAsync();
                downstream.ExpectError().Should().BeOfType<OperationCanceledException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_pass_through_all_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var cancel = new CancellationTokenSource();
                var task = Source.From(Enumerable.Range(1, 100))
                    .Via(cancel.Token.AsFlow<int>())
                    .RunWith(Sink.Seq<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_provide_a_flow_that_if_materialized_multiple_times_with_multiple_types_stops_all_streams_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();

                var t1 = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: true))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var t2 = this.SourceProbe<string>()
                    .Via(cancel.Token.AsFlow<string>(cancelGracefully: true))
                    .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                    .Run(Materializer);

                var upstream1 = t1.Item1;
                var downstream1 = t1.Item2;
                var upstream2 = t2.Item1;
                var downstream2 = t2.Item2;

                await downstream1.RequestAsync(1);
                await upstream1.SendNextAsync(1);
                await downstream1.ExpectNextAsync(1);

                await downstream2.RequestAsync(2);
                await upstream2.SendNext("A").SendNextAsync("B");
                downstream2.ExpectNext("A", "B");

                cancel.Cancel();

                await upstream1.ExpectCancellationAsync();
                await upstream2.ExpectCancellationAsync();
                await downstream1.ExpectCompleteAsync();
                await downstream2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_provide_a_flow_that_if_materialized_multiple_times_with_multiple_types_fails_all_streams_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();

                var t1 = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: false))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var t2 = this.SourceProbe<string>()
                    .Via(cancel.Token.AsFlow<string>(cancelGracefully: false))
                    .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                    .Run(Materializer);

                var upstream1 = t1.Item1;
                var downstream1 = t1.Item2;
                var upstream2 = t2.Item1;
                var downstream2 = t2.Item2;

                await downstream1.RequestAsync(1);
                await upstream1.SendNextAsync(1);
                await downstream1.ExpectNextAsync(1);

                await downstream2.RequestAsync(2);
                await upstream2.SendNext("A").SendNextAsync("B");
                downstream2.ExpectNext("A", "B");

                cancel.Cancel();
                await upstream1.ExpectCancellationAsync();
                await upstream2.ExpectCancellationAsync();

                downstream1.ExpectError().Should().BeOfType<OperationCanceledException>();
                downstream2.ExpectError().Should().BeOfType<OperationCanceledException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_ignore_subsequent_aborts_and_shutdowns_after_shutdown()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();

                var t = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: true))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await downstream.RequestAsync(1);
                await upstream.SendNextAsync(1);
                await downstream.ExpectNextAsync(1);

                cancel.Cancel();
                await upstream.ExpectCancellationAsync();
                await downstream.ExpectCompleteAsync();

                cancel.Cancel();
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                cancel.Cancel();
                await upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_complete_immediately_flows_materialized_after_switch_shutdown()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();
                cancel.Cancel();

                var t = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: true))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await upstream.ExpectCancellationAsync();
                await downstream.ExpectSubscriptionAndCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_must_fail_immediately_flows_materialized_after_switch_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var cancel = new CancellationTokenSource();
                cancel.Cancel();

                var t = this.SourceProbe<int>()
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: false))
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                var upstream = t.Item1;
                var downstream = t.Item2;

                await upstream.ExpectCancellationAsync();
                downstream.ExpectSubscriptionAndError().Should().BeOfType<OperationCanceledException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_CancellationToken_flow_should_not_cause_problems_if_switch_is_shutdown_after_flow_completed_normally()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var cancel = new CancellationTokenSource();
                var task = Source.From(Enumerable.Range(1, 10))
                    .Via(cancel.Token.AsFlow<int>(cancelGracefully: true))
                    .RunWith(Sink.Seq<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                cancel.Cancel();
                return Task.CompletedTask;
            }, Materializer);
        }

        #endregion
    }
}
