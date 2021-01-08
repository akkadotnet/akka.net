//-----------------------------------------------------------------------
// <copyright file="FlowMonitorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowMonitorSpec : AkkaSpec
    {
        public ActorMaterializer Materializer { get; }

        public FlowMonitorSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void A_FlowMonitor_must_return_Finished_when_stream_is_completed()
        {
            var t = this.SourceProbe<int>()
                .Monitor(Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1.Item1;
            var monitor = t.Item1.Item2;
            var sink = t.Item2;
            source.SendComplete();
            AwaitAssert(() =>
            {
                if(monitor.State != FlowMonitor.Finished.Instance)
                    throw new Exception();
            }, TimeSpan.FromSeconds(3));
            source.ExpectRequest();
            sink.ExpectSubscription();
        }

        [Fact]
        public void A_FlowMonitor_must_return_Finished_when_stream_is_cancelled_from_downstream()
        {
            var t = this.SourceProbe<int>()
                .Monitor(Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var monitor = t.Item1;
            var sink = t.Item2;

            sink.Cancel();
            AwaitAssert(() =>
            {
                if (monitor.State != FlowMonitor.Finished.Instance)
                    throw new Exception();
            }, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void A_FlowMonitor_must_return_Failed_when_stream_fails_and_propagate_the_error()
        {
            var t = this.SourceProbe<int>()
                .Monitor(Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1.Item1;
            var monitor = t.Item1.Item2;
            var sink = t.Item2;
            var ex = new TestException("Source failed");
            source.SendError(ex);
            AwaitAssert(() =>
            {
                var state = monitor.State as FlowMonitor.Failed;
                if(state != null && ReferenceEquals(state.Cause, ex))
                    return;

                throw new Exception();
            });
            sink.ExpectSubscriptionAndError().ShouldBe(ex);
        }

        [Fact]
        public void A_FlowMonitor_must_return_Initialized_for_an_empty_stream()
        {
            var t = this.SourceProbe<int>()
                .Monitor(Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1.Item1;
            var monitor = t.Item1.Item2;
            var sink = t.Item2;

            AwaitAssert(() =>
            {
                if (monitor.State != FlowMonitor.Initialized.Instance)
                    throw new Exception();
            }, TimeSpan.FromSeconds(3));
            source.ExpectRequest();
            sink.ExpectSubscription();
        }

        [Fact]
        public void A_FlowMonitor_must_return_Received_after_receiving_a_message()
        {
            var t = this.SourceProbe<int>()
                .Monitor(Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1.Item1;
            var monitor = t.Item1.Item2;
            var sink = t.Item2;
            var message = 42;
            source.SendNext(message);
            sink.RequestNext(message);
            AwaitAssert(() =>
            {
                var state = monitor.State as FlowMonitor.Received<int>;
                if (state != null && state.Message == 42)
                    return;

                throw new Exception();
            });
        }
        
        // Check a stream that processes StreamState messages specifically, to make sure the optimization in FlowMonitorImpl
        // (to avoid allocating an object for each message) doesn't introduce a bug
        [Fact]
        public void A_FlowMonitor_must_return_Received_after_receiving_a_StreamState_message()
        {
            var t = this.SourceProbe<FlowMonitor.Received<string>>()
                .Monitor(Keep.Both)
                .ToMaterialized(this.SinkProbe<FlowMonitor.Received<string>>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1.Item1;
            var monitor = t.Item1.Item2;
            var sink = t.Item2;
            var message = new FlowMonitor.Received<string>("message");
            source.SendNext(message);
            sink.RequestNext(message);
            AwaitAssert(() =>
            {
                var state = monitor.State as FlowMonitor.Received<FlowMonitor.Received<string>>;
                if (state != null && state.Message == message)
                    return;

                throw new Exception();
            });
        }

        [Fact]
        public void A_FlowMonitor_must_return_failed_when_stream_is_abruptly_terminated()
        {
            var materializer = ActorMaterializer.Create(Sys);

            var t = this.SourceProbe<string>().Monitor(Keep.Both).To(Sink.Ignore<string>()).Run(materializer);
            var monitor = t.Item2;

            materializer.Shutdown();
            AwaitAssert(() => monitor.State.Should().BeOfType<FlowMonitor.Failed>(), RemainingOrDefault);
        }
    }
}
