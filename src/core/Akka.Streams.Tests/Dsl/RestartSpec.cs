using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class RestartSpec : Akka.TestKit.Xunit2.TestKit
    {
        private ActorMaterializer Materializer { get; }

        public RestartSpec(ITestOutputHelper output) : base("", null, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void A_restart_with_backoff_source_should_run_normally()
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
        }

        [Fact]
        public void A_restart_with_backoff_source_should_restart_on_completion()
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
        }

        [Fact]
        public void A_restart_with_backoff_source_should_restart_on_failure()
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
        }

        [Fact]
        public void A_restart_with_backoff_source_should_backoff_before_restart()
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
        }

        [Fact]
        public void A_restart_with_backoff_source_should_reset_exponential_backoff_back_to_minimum_when_source_runs_for_at_least_minimum_backoff_without_completing()
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
            probe.RequestNext().Should().Be("a"); // TODO: why I can't set timeout here?

            created.Current.Should().Be(4);

            probe.Cancel();
        }

        [Fact]
        public void A_restart_with_backoff_source_should_cancel_the_currently_running_source_when_cancelled()
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
        }

        [Fact]
        public void A_restart_with_backoff_source_should_not_restart_the_source_when_cancelled_while_backing_off()
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
        }
    }
}
