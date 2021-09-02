//-----------------------------------------------------------------------
// <copyright file="RestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
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
    public class RestartSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        private readonly TimeSpan _shortMinBackoff = TimeSpan.FromMilliseconds(10);
        private readonly TimeSpan _shortMaxBackoff = TimeSpan.FromMilliseconds(20);
        private readonly TimeSpan _minBackoff = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _maxBackoff = TimeSpan.FromSeconds(3);

        private readonly RestartSettings _shortRestartSettings;
        private readonly RestartSettings _restartSettings;

        public RestartSpec(ITestOutputHelper output)
            : base("{}", output)
        {
            Materializer = Sys.Materializer();

            _shortRestartSettings = RestartSettings.Create(_shortMinBackoff, _shortMaxBackoff, 0);
            _restartSettings = RestartSettings.Create(_minBackoff, _maxBackoff, 0);
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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
        public void A_restart_with_backoff_source_should_backoff_before_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" });
                    }, _restartSettings)
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");

                // There should be a delay of at least _minBackoff before we receive the element after restart
                var deadline = (_minBackoff - TimeSpan.FromMilliseconds(1)).FromNow();
                probe.Request(1);

                probe.ExpectNext("a");
                deadline.IsOverdue.Should().Be(true);

                created.Current.Should().Be(2);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" });
                    }, _restartSettings)
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("b");
                // There should be _minBackoff delay
                probe.RequestNext("a");
                probe.RequestNext("b");
                probe.Request(1);
                // The probe should now be backing off again with with increased backoff

                // Now wait for the delay to pass, then it will start the new source, we also want to wait for the
                // subsequent backoff to pass, so it resets the restart count
                Thread.Sleep(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                probe.ExpectNext("a");
                probe.RequestNext("b");

                // We should have reset, so the restart delay should be back, ie we should receive the
                // next element within < 2 * _minBackoff
                probe.RequestNext(TimeSpan.FromTicks(_minBackoff.Ticks * 2) - TimeSpan.FromMilliseconds(10)).Should().Be("a");

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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
                }, _restartSettings).RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.Request(1);
                // Should be backing off now
                probe.Cancel();

                // Wait to ensure it isn't restarted
                Thread.Sleep(_minBackoff + TimeSpan.FromMilliseconds(100));
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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

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
                    }, _shortRestartSettings.WithMaxRestarts(1, _shortMinBackoff))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("a");
                probe.ExpectComplete();

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
                    }, _restartSettings.WithMaxRestarts(2, _minBackoff))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                // There should be minBackoff delay
                probe.RequestNext("a");
                // The probe should now be backing off again with with increased backoff

                // Now wait for the delay to pass, then it will start the new source, we also want to wait for the
                // subsequent backoff to pass
                Thread.Sleep(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                probe.RequestNext("a");
                // We now are able to trigger the third restart, since enough time has elapsed to reset the counter
                probe.RequestNext("a");

                created.Current.Should().Be(4);

                probe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_source_should_allow_using_withMaxRestarts_instead_of_minBackoff_to_determine_the_maxRestarts_reset_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" }).TakeWhile(c => c != "b");
                    }, _shortRestartSettings.WithMaxRestarts(2, TimeSpan.FromSeconds(1)))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.RequestNext("a");
                probe.RequestNext("a");

                Thread.Sleep(_shortMinBackoff + TimeSpan.FromTicks(_shortMinBackoff.Ticks * 2) + _shortMinBackoff); // if using shortMinBackoff as deadline cause reset

                probe.RequestNext("a");

                probe.Request(1);
                probe.ExpectComplete();

                created.Current.Should().Be(3);

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
                }, _shortRestartSettings), Keep.Left).Run(Materializer);

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
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _shortRestartSettings), Keep.Left)
                    .Run(Materializer);

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

        [Fact]
        public void A_restart_with_backoff_sink_should_backoff_before_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _restartSettings), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                probe.SendNext("b");
                var deadline = (_minBackoff - TimeSpan.FromMilliseconds(1)).FromNow();
                sinkProbe.Request(1);
                sinkProbe.ExpectNext("b");
                deadline.IsOverdue.Should().BeTrue();

                created.Current.Should().Be(2);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _restartSettings), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // There should be a minBackoff delay
                probe.SendNext("b");
                sinkProbe.RequestNext("b");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                sinkProbe.Request(1);
                // The probe should now be backing off for 2 * minBackoff

                // Now wait for the 2 * minBackoff delay to pass, then it will start the new source, we also want to wait for the
                // subsequent minBackoff min backoff to pass, so it resets the restart count
                Thread.Sleep(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                // We should have reset, so the restart delay should be back to minBackoff, ie we should definitely receive the
                // next element within < 2 * minBackoff
                probe.SendNext("c");
                sinkProbe.Request(1);
                sinkProbe.ExpectNext(TimeSpan.FromTicks(2 * _minBackoff.Ticks) - TimeSpan.FromMilliseconds(10), "c");

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
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _restartSettings), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("a");
                sinkProbe.RequestNext("a");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // Should be backing off now
                probe.SendComplete();

                // Wait to ensure it isn't restarted
                Thread.Sleep(_minBackoff + TimeSpan.FromMilliseconds(100));
                created.Current.Should().Be(1);

                sinkProbe.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_not_restart_the_sink_when_maxRestarts_is_reached()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _shortRestartSettings.WithMaxRestarts(1, _shortMinBackoff)), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                probe.ExpectCancellation();

                created.Current.Should().Be(2);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_reset_maxRestarts_when_sink_runs_for_at_least_minimum_backoff_without_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _restartSettings.WithMaxRestarts(2, _minBackoff)), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // There should be a minBackoff delay
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // The probe should now be backing off for 2 * minBackoff

                // Now wait for the 2 * minBackoff delay to pass, then it will start the new source, we also want to wait for the
                // subsequent minBackoff to pass, so it resets the restart count
                Thread.Sleep(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                // We now are able to trigger the third restart, since enough time has elapsed to reset the counter
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                created.Current.Should().Be(4);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_restart_with_backoff_sink_should_allow_using_withMaxRestarts_instead_of_minBackoff_to_determine_the_maxRestarts_reset_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var created = new AtomicCounter(0);
                var (queue, sinkProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
                var probe = this.SourceProbe<string>()
                    .ToMaterialized(RestartSink.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<string>().TakeWhile(c => c != "cancel", inclusive: true).To(Sink.ForEach<string>(c => queue.SendNext(c)));
                    }, _shortRestartSettings.WithMaxRestarts(2, TimeSpan.FromSeconds(1))), Keep.Left)
                    .Run(Materializer);

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // There should be a shortMinBackoff delay
                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");
                // The probe should now be backing off for 2 * shortMinBackoff

                Thread.Sleep(_shortMinBackoff + TimeSpan.FromTicks(_shortMinBackoff.Ticks * 2) + _shortMinBackoff); // if using shortMinBackoff as deadline cause reset

                probe.SendNext("cancel");
                sinkProbe.RequestNext("cancel");

                // We cannot get a final element
                probe.SendNext("cancel");
                sinkProbe.Request(1);
                sinkProbe.ExpectNoMsg();

                created.Current.Should().Be(3);

                sinkProbe.Cancel();
                probe.SendComplete();
            }, Materializer);
        }

        //
        // Flow
        //

        /// <summary>
        /// Helps reuse all the SetupFlow code for both methods: WithBackoff, and OnlyOnFailuresWithBackoff
        /// </summary>
        private static Flow<TIn, TOut, NotUsed> RestartFlowFactory<TIn, TOut, TMat>(Func<Flow<TIn, TOut, TMat>> flowFactory, bool onlyOnFailures, RestartSettings settings)
        {
            // choose the correct backoff method
            return onlyOnFailures
                ? RestartFlow.OnFailuresWithBackoff(flowFactory, settings)
                : RestartFlow.WithBackoff(flowFactory, settings);
        }

        private (AtomicCounter, TestPublisher.Probe<string>, TestSubscriber.Probe<string>, TestPublisher.Probe<string>, TestSubscriber.Probe<string>) SetupFlow(
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            int maxRestarts = -1,
            bool onlyOnFailures = false)
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
                }, onlyOnFailures, RestartSettings.Create(minBackoff, maxBackoff, 0).WithMaxRestarts(maxRestarts, minBackoff)), Keep.Left)
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
                var (source, sink) = this.SourceProbe<string>().ViaMaterialized(RestartFlow.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>();
                }, _shortRestartSettings), Keep.Left).ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);

                source.SendNext("a");
                sink.RequestNext("a");
                source.SendNext("b");
                sink.RequestNext("b");

                created.Current.Should().Be(1);
                source.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void Simplified_restart_flow_restarts_stages_test()
        {
            var created = new AtomicCounter(0);
            var restarts = 4;
            this.AssertAllStagesStopped(() =>
            {
                var flow = RestartFlowFactory<int, int, NotUsed>(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<int>()
                            .Select(i =>
                            {
                                if (i == 6)
                                {
                                    throw new ArgumentException($"BOOM");
                                }

                                return i;
                            });
                    }, true,
                    //defaults to unlimited restarts
                    RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(30), 0));

                var (source, sink) = this.SourceProbe<int>().Select(x =>
                    {
                        Log.Debug($"Processing: {x}");
                        return x;
                    })
                    .Via(flow)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                source.SendNext(1);
                source.SendNext(2);
                source.SendNext(3);
                source.SendNext(4);
                source.SendNext(5);
                for (int i = 0; i < restarts; i++)
                {
                    source.SendNext(6);
                }

                source.SendNext(7);
                source.SendNext(8);
                source.SendNext(9);
                source.SendNext(10);

                sink.RequestNext(1);
                sink.RequestNext(2);
                sink.RequestNext(3);
                sink.RequestNext(4);
                sink.RequestNext(5);
                //6 is never received since RestartFlow's do not retry 
                sink.RequestNext(7);
                sink.RequestNext(8);
                sink.RequestNext(9);
                sink.RequestNext(10);

                source.SendComplete();
            }, Materializer);
            created.Current.Should().Be(restarts + 1);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_restart_on_cancellation()
        {
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            source.SendNext("cancel");
            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(), flowInProbe.ExpectNext()).Should()
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
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            sink.Request(1);
            flowOutProbe.SendNext("complete");

            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(), flowInProbe.ExpectNext()).Should()
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
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

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

        [Fact]
        public void A_restart_with_backoff_flow_should_backoff_before_restart()
        {
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_minBackoff, _maxBackoff);

            source.SendNext("a");
            flowInProbe.RequestNext("a");
            flowOutProbe.SendNext("b");
            sink.RequestNext("b");

            // we need to start counting time before we issue the cancel signal,
            // as starting the counter anywhere after the cancel signal, might not
            // capture all of the time, that has been spent for the backoff.
            var deadline = _minBackoff.FromNow();

            source.SendNext("cancel");
            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(), flowInProbe.ExpectNext()).Should()
                .Contain(ImmutableList.Create("in complete", "out complete"));

            source.SendNext("c");
            flowInProbe.Request(1);
            flowInProbe.ExpectNext("c");
            deadline.IsOverdue.Should().BeTrue();

            created.Current.Should().Be(2);
        }

        [Fact]
        public void A_restart_with_backoff_flow_should_continue_running_flow_out_port_after_in_has_been_sent_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _maxBackoff);

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
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _maxBackoff);

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

        [Fact]
        public void A_restart_with_backoff_flow_should_not_restart_on_completion_when_maxRestarts_is_reached()
        {
            var (created, _, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, maxRestarts: 1);

            sink.Request(1);
            flowOutProbe.SendNext("complete");

            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            ImmutableList.Create(flowInProbe.ExpectNext(), flowInProbe.ExpectNext()).Should()
                .Contain(ImmutableList.Create("in complete", "out complete"));

            // and it should restart
            sink.Request(1);
            flowOutProbe.SendNext("complete");

            // This will complete the flow in probe and cancel the flow out probe
            flowInProbe.Request(2);
            flowInProbe.ExpectNext("out complete");
            flowInProbe.ExpectNoMsg(TimeSpan.FromTicks(_shortMinBackoff.Ticks * 3));
            sink.ExpectComplete();

            created.Current.Should().Be(2);
        }

        // onlyOnFailures 
        [Fact]
        public void A_restart_with_backoff_flow_should_stop_on_cancellation_when_using_onlyOnFailuresWithBackoff()
        {
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

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
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

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
            var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

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
            sink.Request(1);
            created.Current.Should().Be(2);
        }
    }
}