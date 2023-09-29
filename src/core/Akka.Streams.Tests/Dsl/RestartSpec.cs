//-----------------------------------------------------------------------
// <copyright file="RestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using Akka.Tests.Shared.Internals;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class RestartSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        private readonly TimeSpan _shortMinBackoff = TimeSpan.FromMilliseconds(10);
        private readonly TimeSpan _shortMaxBackoff = TimeSpan.FromMilliseconds(20);
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;

        private readonly RestartSettings _shortRestartSettings;
        private readonly RestartSettings _restartSettings;

        public RestartSpec(ITestOutputHelper output)
            : base("{}", output)
        {
            Materializer = Sys.Materializer();

            _minBackoff = Dilated(TimeSpan.FromSeconds(1));
            _maxBackoff = Dilated(TimeSpan.FromSeconds(3));

            _shortRestartSettings = RestartSettings.Create(_shortMinBackoff, _shortMaxBackoff, 0);
            _restartSettings = RestartSettings.Create(_minBackoff, _maxBackoff, 0);
        }

        //
        // Source
        //

        [Fact]
        public async Task A_restart_with_backoff_source_should_run_normally()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Repeat("a");
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "a", "a", "a", "a")
                    .ExecuteAsync();

                created.Current.Should().Be(1);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_restart_on_completion()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.From(new List<string> { "a", "b" });
                }, _shortRestartSettings).RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b", "a", "b", "a")
                    .ExecuteAsync();

                created.Current.Should().Be(3);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_restart_on_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b", "a", "b", "a")
                    .ExecuteAsync();

                created.Current.Should().Be(3);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_backoff_before_restart()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" });
                    }, _restartSettings)
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b")
                    .ExecuteAsync();

                // There should be a delay of at least _minBackoff before we receive the element after restart
                var deadline = (_minBackoff - TimeSpan.FromMilliseconds(100)).FromNow();

                await probe.AsyncBuilder()
                    .Request(1)
                    .ExpectNext("a")
                    .ExecuteAsync();
                
                deadline.IsOverdue.Should().Be(true);

                created.Current.Should().Be(2);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" });
                    }, _restartSettings)
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b")
                    // There should be _minBackoff delay
                    .RequestNextN("a", "b")
                    .Request(1)
                    .ExecuteAsync();
                // The probe should now be backing off again with with increased backoff

                // Now wait for the delay to pass, then it will start the new source, we also want to wait for the
                // subsequent backoff to pass, so it resets the restart count
                await Task.Delay(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b")
                    .ExecuteAsync();

                // We should have reset, so the restart delay should be back, ie we should receive the
                // next element within < 2 * _minBackoff
                (await probe.RequestNextAsync(TimeSpan.FromTicks(_minBackoff.Ticks * 2) - TimeSpan.FromMilliseconds(10)))
                    .Should().Be("a");

                created.Current.Should().Be(4);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_cancel_the_currently_running_source_when_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.AsyncBuilder()
                    .RequestNext("a")
                    .Cancel()
                    .ExecuteAsync();

                tcs.Task.Result.Should().BeSameAs(Done.Instance);

                // Wait to ensure it isn't restarted
                await Task.Delay(200);
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_not_restart_the_source_when_cancelled_while_backing_off()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Source.Single("a");
                }, _restartSettings).RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNext("a")
                    .Request(1)
                    // Should be backing off now
                    .Cancel()
                    .ExecuteAsync();

                // Wait to ensure it isn't restarted
                await Task.Delay(_minBackoff + TimeSpan.FromMilliseconds(100));
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_stop_on_completion_if_it_should_only_be_restarted_in_failures()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b")
                    // will fail, and will restart
                    .RequestNextN("a", "b", "c")
                    .ExecuteAsync();

                created.Current.Should().Be(2);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_restart_on_failure_when_only_due_to_failures_should_be_restarted()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.AsyncBuilder()
                    .RequestNextN("a", "b", "a", "b", "a")
                    .ExecuteAsync();

                created.Current.Should().Be(3);

                await probe.CancelAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Flaky test, ExpectComplete times out with the default 3 seconds value under heavy load")]
        public async Task A_restart_with_backoff_source_should_not_restart_the_source_when_maxRestarts_is_reached()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.Single("a");
                    }, _shortRestartSettings.WithMaxRestarts(1, _shortMinBackoff))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "a")
                    .ExpectComplete()
                    .ExecuteAsync();

                created.Current.Should().Be(2);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_reset_maxRestarts_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.Single("a");
                    }, _restartSettings.WithMaxRestarts(2, _minBackoff))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNext("a")
                    // There should be minBackoff delay
                    .RequestNext("a")
                    .ExecuteAsync();
                // The probe should now be backing off again with with increased backoff

                // Now wait for the delay to pass, then it will start the new source, we also want to wait for the
                // subsequent backoff to pass
                await Task.Delay(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                await probe.AsyncBuilder()
                    .RequestNext("a")
                    // We now are able to trigger the third restart, since enough time has elapsed to reset the counter
                    .RequestNext("a")
                    .ExecuteAsync();

                created.Current.Should().Be(4);

                await probe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_source_should_allow_using_withMaxRestarts_instead_of_minBackoff_to_determine_the_maxRestarts_reset_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var probe = RestartSource.WithBackoff(() =>
                    {
                        created.IncrementAndGet();
                        return Source.From(new List<string> { "a", "b" }).TakeWhile(c => c != "b");
                    }, _shortRestartSettings.WithMaxRestarts(2, TimeSpan.FromSeconds(1)))
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.AsyncBuilder()
                    .RequestNextN("a", "a")
                    .ExecuteAsync();

                await Task.Delay(_shortMinBackoff + TimeSpan.FromTicks(_shortMinBackoff.Ticks * 2) + _shortMinBackoff); // if using shortMinBackoff as deadline cause reset

                await probe.AsyncBuilder()
                    .RequestNext("a")
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();

                created.Current.Should().Be(3);

                await probe.CancelAsync();
            }, Materializer);
        }

        //
        // Sink
        //

        [Fact]
        public async Task A_restart_with_backoff_sink_should_run_normally()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.AsyncBuilder()
                    .SendNext("a")
                    .SendNext("b")
                    .SendNext("c")
                    .SendComplete()
                    .ExecuteAsync();

                await tcs.Task.ShouldCompleteWithin(3.Seconds());
                tcs.Task.Result.Should().ContainInOrder("a", "b", "c");
                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_restart_on_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("a");
                await sinkProbe.RequestNextAsync("a");
                await probe.SendNextAsync("b");
                await sinkProbe.RequestNextAsync("b");
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                await probe.SendNextAsync("c");
                await sinkProbe.RequestNextAsync("c");
                await probe.SendCompleteAsync();

                created.Current.Should().Be(2);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_backoff_before_restart()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("a");
                await sinkProbe.RequestNextAsync("a");
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                await probe.SendNextAsync("b");
                var deadline = (_minBackoff - TimeSpan.FromMilliseconds(1)).FromNow();
                await sinkProbe.RequestAsync(1);
                await sinkProbe.ExpectNextAsync("b");
                deadline.IsOverdue.Should().BeTrue();

                created.Current.Should().Be(2);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("a");
                await sinkProbe.RequestNextAsync("a");
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // There should be a minBackoff delay
                await probe.SendNextAsync("b");
                await sinkProbe.RequestNextAsync("b");
                await probe.SendNextAsync("cancel");

                await sinkProbe.AsyncBuilder()
                    .RequestNext("cancel")
                    .Request(1)
                    .ExecuteAsync();
                // The probe should now be backing off for 2 * minBackoff

                // Now wait for the 2 * minBackoff delay to pass, then it will start the new source, we also want to wait for the
                // subsequent minBackoff min backoff to pass, so it resets the restart count
                await Task.Delay(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");

                // We should have reset, so the restart delay should be back to minBackoff, ie we should definitely receive the
                // next element within < 2 * minBackoff
                await probe.SendNextAsync("c");

                await sinkProbe.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(TimeSpan.FromTicks(2 * _minBackoff.Ticks) - TimeSpan.FromMilliseconds(10), "c")
                    .ExecuteAsync();

                created.Current.Should().Be(4);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_not_restart_the_sink_when_completed_while_backing_off()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("a");
                await sinkProbe.RequestNextAsync("a");
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // Should be backing off now
                await probe.SendCompleteAsync();

                // Wait to ensure it isn't restarted
                await Task.Delay(_minBackoff + TimeSpan.FromMilliseconds(100));
                created.Current.Should().Be(1);

                await sinkProbe.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_not_restart_the_sink_when_maxRestarts_is_reached()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");

                await probe.ExpectCancellationAsync();

                created.Current.Should().Be(2);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_reset_maxRestarts_when_sink_runs_for_at_least_minimum_backoff_without_completing()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // There should be a minBackoff delay
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // The probe should now be backing off for 2 * minBackoff

                // Now wait for the 2 * minBackoff delay to pass, then it will start the new source, we also want to wait for the
                // subsequent minBackoff to pass, so it resets the restart count
                Thread.Sleep(_minBackoff + TimeSpan.FromTicks(_minBackoff.Ticks * 2) + _minBackoff + TimeSpan.FromMilliseconds(500));

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");

                // We now are able to trigger the third restart, since enough time has elapsed to reset the counter
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");

                created.Current.Should().Be(4);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_sink_should_allow_using_withMaxRestarts_instead_of_minBackoff_to_determine_the_maxRestarts_reset_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // There should be a shortMinBackoff delay
                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");
                // The probe should now be backing off for 2 * shortMinBackoff

                await Task.Delay(_shortMinBackoff + TimeSpan.FromTicks(_shortMinBackoff.Ticks * 2) + _shortMinBackoff); // if using shortMinBackoff as deadline cause reset

                await probe.SendNextAsync("cancel");
                await sinkProbe.RequestNextAsync("cancel");

                // We cannot get a final element
                await probe.SendNextAsync("cancel");
                await sinkProbe.AsyncBuilder()
                    .Request(1)
                    .ExpectNoMsg()
                    .ExecuteAsync();

                created.Current.Should().Be(3);

                await sinkProbe.CancelAsync();
                await probe.SendCompleteAsync();
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
            var (flowInSource, flowInProbe) = this.SourceProbe<string>().ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);
            var (flowOutProbe, flowOutSource) = this.SourceProbe<string>().ToMaterialized(BroadcastHub.Sink<string>(), Keep.Both).Run(Materializer);

            // We can't just use ordinary probes here because we're expecting them to get started/restarted. Instead, we
            // simply use the probes as a message bus for feeding and capturing events.
            var (source, sink) = this.SourceProbe<string>().ViaMaterialized(RestartFlowFactory(() =>
                {
                    created.IncrementAndGet();
                    var snk = Flow.Create<string>()
                        .TakeWhile(s => s != "cancel")
                        .To(Sink.ForEach<string>(c => flowInSource.SendNext(c))
                            .MapMaterializedValue(task => 
                                task.ShouldCompleteWithin(10.Seconds()).ContinueWith(
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
                        task.ShouldCompleteWithin(10.Seconds()).ContinueWith(_ =>
                        {
                            flowInSource.SendNext("out complete");
                            return NotUsed.Instance;
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                        return s1;
                    });

                    return Flow.FromSinkAndSource(snk, src);
                }, onlyOnFailures, RestartSettings.Create(minBackoff, maxBackoff, 0).WithMaxRestarts(maxRestarts, minBackoff)), Keep.Left)
                .ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);

            return (created, source, flowInProbe, flowOutProbe, sink);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_run_normally()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                var (source, sink) = this.SourceProbe<string>().ViaMaterialized(RestartFlow.WithBackoff(() =>
                {
                    created.IncrementAndGet();
                    return Flow.Create<string>();
                }, _shortRestartSettings), Keep.Left).ToMaterialized(this.SinkProbe<string>(), Keep.Both).Run(Materializer);

                await source.SendNextAsync("a");
                await sink.RequestNextAsync("a");
                await source.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                created.Current.Should().Be(1);
                await source.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Simplified_restart_flow_restarts_stages_test()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var created = new AtomicCounter(0);
                const int restarts = 4;
            
                var flow = RestartFlowFactory(() =>
                    {
                        created.IncrementAndGet();
                        return Flow.Create<int>()
                            .Select(i =>
                            {
                                if (i == 6)
                                {
                                    throw new ArgumentException("BOOM");
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

                await source.AsyncBuilder()
                    .SendNext(new[] { 1, 2, 3, 4, 5 })
                    .SendNext(Enumerable.Repeat(6, restarts))
                    .SendNext(new[] { 7, 8, 9, 10 })
                    .ExecuteAsync();

                await sink.AsyncBuilder()
                    //6 is never received since RestartFlow's do not retry 
                    .RequestNextN(1, 2, 3, 4, 5, 7, 8, 9, 10)
                    .ExecuteAsync();
                    
                await source.SendCompleteAsync();
                
                created.Current.Should().Be(restarts + 1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_restart_on_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await source.SendNextAsync("cancel");
                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.RequestAsync(2);
                (await flowInProbe.ExpectNextNAsync(2, 3.Seconds()).ToListAsync())
                    .Should().Contain(new []{"in complete", "out complete"});

                // and it should restart
                await source.SendNextAsync("c");
                await flowInProbe.RequestNextAsync("c");
                await flowOutProbe.SendNextAsync("d");
                await sink.RequestNextAsync("d");

                created.Current.Should().Be(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_restart_on_completion()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await sink.RequestAsync(1);
                await flowOutProbe.SendNextAsync("complete");

                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.RequestAsync(2);
                (await flowInProbe.ExpectNextNAsync(2, 3.Seconds()).ToListAsync())
                    .Should().Contain(new []{"in complete", "out complete"});

                // and it should restart
                await source.SendNextAsync("c");
                await flowInProbe.RequestNextAsync("c");
                await flowOutProbe.SendNextAsync("d");
                await sink.RequestNextAsync("d");

                created.Current.Should().Be(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_restart_on_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await sink.RequestAsync(1);
                await flowOutProbe.SendNextAsync("error");

                // This should complete the in probe
                await flowInProbe.RequestNextAsync("in complete");

                // and it should restart
                await source.SendNextAsync("c");
                await flowInProbe.RequestNextAsync("c");
                await flowOutProbe.SendNextAsync("d");
                await sink.RequestNextAsync("d");

                created.Current.Should().Be(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_backoff_before_restart()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_minBackoff, _maxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                // we need to start counting time before we issue the cancel signal,
                // as starting the counter anywhere after the cancel signal, might not
                // capture all of the time, that has been spent for the backoff.
                var deadline = _minBackoff.FromNow();

                await source.SendNextAsync("cancel");
                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.RequestAsync(2);
                (await flowInProbe.ExpectNextNAsync(2, 3.Seconds()).ToListAsync())
                    .Should().Contain(new []{"in complete", "out complete"});

                await source.SendNextAsync("c");
                await flowInProbe.RequestAsync(1);
                await flowInProbe.ExpectNextAsync("c");
                deadline.IsOverdue.Should().BeTrue();

                created.Current.Should().Be(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_continue_running_flow_out_port_after_in_has_been_sent_completion()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _maxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await source.SendCompleteAsync();
                await flowInProbe.RequestNextAsync("in complete");

                await flowOutProbe.SendNextAsync("c");
                await sink.RequestNextAsync("c");
                await flowOutProbe.SendNextAsync("d");
                await sink.RequestNextAsync("d");

                await sink.RequestAsync(1);
                await flowOutProbe.SendCompleteAsync();
                await flowInProbe.RequestNextAsync("out complete");
                await sink.ExpectCompleteAsync();

                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_continue_running_flow_in_port_after_out_has_been_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _maxBackoff);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await sink.CancelAsync();
                await flowInProbe.RequestNextAsync("out complete");

                await source.SendNextAsync("c");
                await flowInProbe.RequestNextAsync("c");
                await source.SendNextAsync("d");
                await flowInProbe.RequestNextAsync("d");

                await source.SendNextAsync("cancel");
                await flowInProbe.RequestNextAsync("in complete");
                await source.ExpectCancellationAsync();

                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_not_restart_on_completion_when_maxRestarts_is_reached()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, _, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, maxRestarts: 1);

                await sink.RequestAsync(1);
                await flowOutProbe.SendNextAsync("complete");

                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.RequestAsync(2);
                (await flowInProbe.ExpectNextNAsync(2, 3.Seconds()).ToListAsync())
                    .Should().Contain(new []{"in complete", "out complete"});

                // and it should restart
                await sink.RequestAsync(1);
                await flowOutProbe.SendNextAsync("complete");

                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.AsyncBuilder()
                    .Request(2)
                    .ExpectNext("out complete")
                    .ExpectNoMsg(TimeSpan.FromTicks(_shortMinBackoff.Ticks * 3))
                    .ExecuteAsync();
                await sink.ExpectCompleteAsync();

                created.Current.Should().Be(2);
                
            }, Materializer);
        }

        // onlyOnFailures 
        [Fact]
        public async Task A_restart_with_backoff_flow_should_stop_on_cancellation_when_using_onlyOnFailuresWithBackoff()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await source.SendNextAsync("cancel");
                // This will complete the flow in probe and cancel the flow out probe
                await flowInProbe.AsyncBuilder()
                    .Request(2)
                    .ExpectNext("in complete")
                    .ExecuteAsync();

                await source.ExpectCancellationAsync();

                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_stop_on_completion_when_using_onlyOnFailuresWithBackoff()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await flowOutProbe.SendNextAsync("complete");
                await sink.RequestAsync(1);
                await sink.ExpectCompleteAsync();

                created.Current.Should().Be(1);
            }, Materializer);
        }

        [Fact]
        public async Task A_restart_with_backoff_flow_should_restart_on_failure_when_using_onlyOnFailuresWithBackoff()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (created, source, flowInProbe, flowOutProbe, sink) = SetupFlow(_shortMinBackoff, _shortMaxBackoff, -1, true);

                await source.SendNextAsync("a");
                await flowInProbe.RequestNextAsync("a");
                await flowOutProbe.SendNextAsync("b");
                await sink.RequestNextAsync("b");

                await sink.RequestAsync(1);
                await flowOutProbe.SendNextAsync("error");

                // This should complete the in probe
                await flowInProbe.RequestNextAsync("in complete");

                // and it should restart
                await source.SendNextAsync("c");
                await flowInProbe.RequestNextAsync("c");
                await flowOutProbe.SendNextAsync("d");
                await sink.RequestNextAsync("d");
                
                created.Current.Should().Be(2);
            }, Materializer);
        }
    }
}
