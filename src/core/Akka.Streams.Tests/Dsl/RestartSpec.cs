//-----------------------------------------------------------------------
// <copyright file="RestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class RestartSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public RestartSpec(ITestOutputHelper output) : base("{}", output)
        {
            Materializer = Sys.Materializer();
        }

        //
        // Source
        //

        [Fact]
        public void A_restart_with_backoff_source_should_run_normally()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Repeat("a");
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("a");
                probe.RequestNext("a");
                probe.RequestNext("a");
                probe.RequestNext("a");

                created.Current.Should().Be(1);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_restart_on_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.From(new List<string> { "a", "b" });
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");

                created.Current.Should().Be(3);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_restart_on_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    var enumerable = new List<string> { "a", "b", "c" }.Select(c =>
                    {
                        if (c == "c")
                            throw new ArgumentException("failed");
                        return c;
                    });
                    return Source.From(enumerable);
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");

                created.Current.Should().Be(3);

                probe.Cancel();
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_restart_with_backoff_source_should_backoff_before_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.From(new List<string> { "a", "b" });
                }, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(1000), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.Request(1);
                // There should be a delay of at least 200ms before we receive the element, wait for 100ms.
                var deadline = TimeSpan.FromMilliseconds(100).FromNow();
                // But the delay shouldn't be more than 300ms.
                probe.ExpectNext(TimeSpan.FromMilliseconds(300), "a");
                deadline.IsOverdue.Should().Be(true);

                created.Current.Should().Be(2);

                probe.Cancel();
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_restart_with_backoff_source_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.From(new List<string> { "a", "b" });
                }, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(2000), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                // There should be a 200ms delay
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.Request(1);
                // The probe should now be backing off for 400ms

                // Now wait for the 400ms delay to pass, then it will start the new source, we also want to wait for the
                // subsequent 200ms min backoff to pass, so it resets the restart count
                Thread.Sleep(700);
                probe.ExpectNext("a");
                probe.RequestNext("b");

                // We should have reset, so the restart delay should be back to 200ms, ie we should definitely receive the
                // next element within 300ms
                probe.RequestNext(TimeSpan.FromMilliseconds(300)).Should().Be("a");

                created.Current.Should().Be(4);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_cancel_the_currently_running_source_when_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tcs = new TaskCompletionSource<Done>();
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.From(new List<string> { "a", "b" })
                        .WatchTermination((source, _) =>
                        {
                            tcs.SetResult(Done.Instance);
                            return source;
                        });
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.Cancel();

                tcs.Task.Result.Should().BeSameAs(Done.Instance);

                // Wait to ensure it isn't restarted
                Thread.Sleep(200);
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_not_restart_the_source_when_cancelled_while_backing_off()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Single("a");
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.Request(1);
                // Should be backing off now
                probe.Cancel();

                // Wait to ensure it isn't restarted
                Thread.Sleep(300);
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_stop_on_completion_if_it_should_only_be_restarted_in_failures()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.OnFailuresWithBackoff(() =>
                {
                    created.IncrementAndGet();
                    var enumerable = new List<string> { "a", "b", "c" }.Select(c =>
                    {
                        switch (c)
                        {
                            case "c": return created.Current == 1 ? throw new ArgumentException("failed") : "c";
                            default: return c;
                        }
                    });
                    return Source.From(enumerable);
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                // will fail, and will restart
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("c");

                created.Current.Should().Be(2);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_restart_on_failure_when_only_due_to_failures_should_be_restarted()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.OnFailuresWithBackoff(() =>
                {
                    created.IncrementAndGet();
                    var enumerable = new List<string> { "a", "b", "c" }.Select(c =>
                    {
                        if (c == "c")
                            throw new ArgumentException("failed");
                        return c;
                    });
                    return Source.From(enumerable);
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.RequestNext("a");

                created.Current.Should().Be(3);

                probe.Cancel();
            }, Materializer);
        }

        // Flaky test, ExpectComplete times out with the default 3 seconds value under heavy load.
        // Fail rate was 1:500
        [Fact]
        public void A_restart_with_backoff_source_should_not_restart_the_source_when_maxRestarts_is_reached()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Single("a");
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0, maxRestarts: 1).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("a");
                probe.ExpectComplete(TimeSpan.FromSeconds(5));

                created.Current.Should().Be(2);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_reset_maxRestarts_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Single("a");
                }, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 0, maxRestarts: 2).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                // There should be minBackoff delay
                probe.RequestNext("a");
                // The probe should now be backing off again with with increased backoff

                // Now wait for the delay to pass, then it will start the new source, we also want to wait for the
                // subsequent backoff to pass
                const int minBackoff = 1000;
                Thread.Sleep((minBackoff + (minBackoff * 2) + minBackoff + 500));

                probe.RequestNext("a");
                // We now are able to trigger the third restart, since enough time has elapsed to reset the counter
                probe.RequestNext("a");

                created.Current.Should().Be(4);

                probe.Cancel();
            }, Materializer);
        }

        //
        // Sink
        //

        [Fact]
        public void A_restart_with_backoff_sink_should_run_normally()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tcs = new TaskCompletionSource<IEnumerable<string>>();
                var probe = this.SourceProbe<string>().ToMaterialized(RestartSink.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Sink.Seq<string>().MapMaterializedValue(task =>
                    {
                        task.ContinueWith(c => tcs.SetResult(c.Result));
                        return Done.Instance;
                    });
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0), Keep.Left).Run(Materializer);

                probe.SendNext("a");
                probe.SendNext("b");
                probe.SendNext("c");
                probe.SendComplete();

                tcs.Task.Result.Should().ContainInOrder("a", "b", "c");
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_restart_on_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tuple = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var queue = tuple.Item1;
                var sinkProbe = tuple.Item2;
                var probe = this.SourceProbe<string>().ToMaterialized(RestartSink.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true)
                        .To(Sink.ForEach<string>(c => queue.SendNext(c)));
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0), Keep.Left).Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("b");
                sinkProbe.RequestNext("b");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                probe.SendNext("c");
                sinkProbe.RequestNext("c");
                probe.SendComplete();

                created.Current.Should().Be(2);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_restart_with_backoff_sink_should_backoff_before_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tuple = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var queue = tuple.Item1;
                var sinkProbe = tuple.Item2;
                var probe = this.SourceProbe<string>().ToMaterialized(RestartSink.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true)
                        .To(Sink.ForEach<string>(c => queue.SendNext(c)));
                }, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2), 0), Keep.Left).Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                probe.SendNext("b");
                sinkProbe.Request(1);
                var deadline = TimeSpan.FromMilliseconds(100).FromNow();
                sinkProbe.ExpectNext(TimeSpan.FromMilliseconds(300), "b");
                deadline.IsOverdue.Should().BeTrue();

                created.Current.Should().Be(2);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_restart_with_backoff_sink_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tuple = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var queue = tuple.Item1;
                var sinkProbe = tuple.Item2;
                var probe = this.SourceProbe<string>().ToMaterialized(RestartSink.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true)
                        .To(Sink.ForEach<string>(c => queue.SendNext(c)));
                }, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2), 0), Keep.Left).Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // There should be a 200ms delay
                probe.SendNext("b");
                sinkProbe.RequestNext("b");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                sinkProbe.Request(1);
                // The probe should now be backing off for 400ms

                // Now wait for the 400ms delay to pass, then it will start the new source, we also want to wait for the
                // subsequent 200ms min backoff to pass, so it resets the restart count
                Thread.Sleep(700);

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                // We should have reset, so the restart delay should be back to 200ms, ie we should definitely receive the
                // next element within 300ms
                probe.SendNext("c");
                sinkProbe.Request(1);
                sinkProbe.ExpectNext(TimeSpan.FromMilliseconds(300), "c");

                created.Current.Should().Be(4);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_not_restart_the_sink_when_completed_while_backing_off()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tuple = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var queue = tuple.Item1;
                var sinkProbe = tuple.Item2;
                var probe = this.SourceProbe<string>().ToMaterialized(RestartSink.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true)
                        .To(Sink.ForEach<string>(c => queue.SendNext(c)));
                }, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2), 0), Keep.Left).Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // Should be backing off now
                probe.SendComplete();

                // Wait to ensure it isn't restarted
                Thread.Sleep(300);
                created.Current.Should().Be(1);

                sinkProbe.Cancel();
            }, Materializer);
        }

        //
        // Flow
        //

        /// <summary>
        /// Helps reuse all the SetupFlow code for both methods: WithBackoff, and OnlyOnFailuresWithBackoff
        /// </summary>
        private static Flow<TIn, TOut, NotUsed> RestartFlowFactory<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts, bool onlyOnFailures)
        {
            // choose the correct backoff method
            return onlyOnFailures
                ? RestartFlow.OnFailuresWithBackoff(flowFactory, minBackoff, maxBackoff, randomFactor, maxRestarts)
                : RestartFlow.WithBackoff(flowFactory, minBackoff, maxBackoff, randomFactor, maxRestarts);
        }

        private (AtomicCounter, TestPublisher.Probe<string>, TestSubscriber.Probe<string>, TestPublisher.Probe<string>, TestSubscriber.Probe<string>) SetupFlow(TimeSpan minBackoff, TimeSpan maxBackoff, int maxRestarts = -1, bool onlyOnFailures = false)
        {
            var created = new AtomicCounter(0);
            var probe1 = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
            var flowInSource = probe1.Item1;
            var flowInProbe = probe1.Item2;
            var probe2 = this.SourceProbe<string>().ToMaterialized(BroadcastHub.Sink<string>(), Keep.Both).Run(Materializer);
            var flowOutProbe = probe2.Item1;
            var flowOutSource = probe2.Item2;

            // We can't just use ordinary probes here because we're expecting them to get started/restarted. Instead, we
            // simply use the probes as a message bus for feeding and capturing events.
            var probe3 = this.SourceProbe<string>().ViaMaterialized(RestartFlowFactory(() =>
                {
                    created.IncrementAndGet();
                    var snk = Flow.Create<string>()
                        .TakeWhile(s => s != "cancel")
                        .To(Sink.ForEach<string>(c => flowInSource.SendNext(c))
                            .MapMaterializedValue(task => task.ContinueWith(
                                t1 =>
                                {
                                    if (t1.IsFaulted || t1.IsCanceled)
                                        flowInSource.SendNext("in error");
                                    else
                                        flowInSource.SendNext("in complete");
                                })));

                    var src = flowOutSource.TakeWhile(s => s != "complete").Select(c =>
                    {
                        if (c == "error")
                            throw new ArgumentException("failed");
                        return c;
                    }).WatchTermination((s1, task) =>
                    {
                        task.ContinueWith(_ =>
                        {
                            flowInSource.SendNext("out complete");
                            return NotUsed.Instance;
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                        return s1;
                    });

                    return Flow.FromSinkAndSource(snk, src);
                }, minBackoff, maxBackoff, 0, maxRestarts, onlyOnFailures), Keep.Left)
                .ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
            var source = probe3.Item1;
            var sink = probe3.Item2;

            return (created, source, flowInProbe, flowOutProbe, sink);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_run_normally()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var tuple = this.SourceProbe<string>().ViaMaterialized(RestartFlow.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>();
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0), Keep.Left).ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var source = tuple.Item1;
                var sink = tuple.Item2;

                source.SendNext("a");
                sink.RequestNext("a");
                source.SendNext("b");
                sink.RequestNext("b");

                created.Current.Should().Be(1);
                source.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_restart_on_cancellation()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            source.SendNext("cancel");
            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(TimeSpan.FromSeconds(5)), flowInProbe.ExpectNext(TimeSpan.FromSeconds(5))).Should()
                .Contain(ImmutableList.Create("in complete", "out complete"));

            // and it should restart
            source.SendNext("c");
            flowInProbe.RequestNext("c");
            flowOutProbe.SendNext("d");
            sink.RequestNext("d");

            created.Current.Should().Be(2);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_restart_on_completion()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            sink.Request(1);
            flowOutProbe.SendNext("complete");

            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(TimeSpan.FromSeconds(5)), flowInProbe.ExpectNext(TimeSpan.FromSeconds(5))).Should()
                .Contain(ImmutableList.Create("in complete", "out complete"));

            // and it should restart
            source.SendNext("c");
            flowInProbe.RequestNext("c");
            flowOutProbe.SendNext("d");
            sink.RequestNext("d");

            created.Current.Should().Be(2);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_restart_on_failure()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            sink.Request(1);
            flowOutProbe.SendNext("error");

            // This should complete the in probe
            flowInProbe.RequestNext("in complete");

            // and it should restart
            source.SendNext("c");
            flowInProbe.RequestNext("c");
            flowOutProbe.SendNext("d");
            sink.RequestNext("d");

            created.Current.Should().Be(2);
        }

        [Fact(Skip ="Racy")]
        public void A_restart_with_backoff_flow_should_backoff_before_restart()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            source.SendNext("cancel");
            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(TimeSpan.FromSeconds(5)), flowInProbe.ExpectNext(TimeSpan.FromSeconds(5))).Should()
                .Contain(ImmutableList.Create("in complete", "out complete"));

            source.SendNext("c");
            flowInProbe.Request(1);
            var deadline = TimeSpan.FromMilliseconds(100).FromNow();
            flowInProbe.ExpectNext(TimeSpan.FromMilliseconds(300), "c");
            deadline.IsOverdue.Should().BeTrue();

            created.Current.Should().Be(2);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_continue_running_flow_out_port_after_in_has_been_sent_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var tuple = SetupFlow(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));
                var created = tuple.Item1;
                var source = tuple.Item2;
                var flowInProbe = tuple.Item3;
                var flowOutProbe = tuple.Item4;
                var sink = tuple.Item5;

                source.SendNext("a");
                flowInProbe.RequestNext("a");
                flowOutProbe.SendNext("b");
                sink.RequestNext("b");

                source.SendComplete();
                flowInProbe.RequestNext("in complete");

                flowOutProbe.SendNext("c");
                sink.RequestNext("c");
                flowOutProbe.SendNext("d");
                sink.RequestNext("d");

                sink.Request(1);
                flowOutProbe.SendComplete();
                flowInProbe.RequestNext("out complete");
                sink.ExpectComplete();

                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_continue_running_flow_in_port_after_out_has_been_cancelled()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            sink.Cancel();
            flowInProbe.RequestNext("out complete");

            source.SendNext("c");
            flowInProbe.RequestNext("c");
            source.SendNext("d");
            flowInProbe.RequestNext("d");

            source.SendNext("cancel");
            flowInProbe.RequestNext("in complete");
            source.ExpectCancellation();

            created.Current.Should().Be(1);
        }

        // onlyOnFailures 
        [Fact]
        public void A_restart_with_backoff_flow_should_stop_on_cancellation_when_using_onlyOnFailuresWithBackoff()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), -1, true);
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            source.SendNext("cancel");
            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            flowInProbe.ExpectNext("in complete");

            source.ExpectCancellation();

            created.Current.Should().Be(1);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_stop_on_completion_when_using_onlyOnFailuresWithBackoff()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), -1, true);
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            flowOutProbe.SendNext("complete");
            sink.Request(1);
            sink.ExpectComplete();

            created.Current.Should().Be(1);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_restart_on_failure_when_using_onlyOnFailuresWithBackoff()
        {
            var tuple = SetupFlow(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), -1, true);
            var created = tuple.Item1;
            var source = tuple.Item2;
            var flowInProbe = tuple.Item3;
            var flowOutProbe = tuple.Item4;
            var sink = tuple.Item5;

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            sink.Request(1);
            flowOutProbe.SendNext("error");

            // This should complete the in probe
            flowInProbe.RequestNext("in complete");

            // and it should restart
            source.SendNext("c");
            flowInProbe.RequestNext("c");
            flowOutProbe.SendNext("d");
            sink.RequestNext("d");

            created.Current.Should().Be(2);
        }
    }
}
