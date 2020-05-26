//-----------------------------------------------------------------------
// <copyright file="QueueSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class QueueSinkSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;
        private readonly TimeSpan _pause = TimeSpan.FromMilliseconds(300);

        private static TestException TestException()
        {
            return new TestException("boom");
        }

        public QueueSinkSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void QueueSink_should_send_the_elements_as_result_of_future()
        {
            this.AssertAllStagesStopped(() =>
            {
                var expected = new List<Option<int>>
                {
                    new Option<int>(1),
                    new Option<int>(2),
                    new Option<int>(3),
                    new Option<int>()
                };
                var queue = Source.From(expected.Where(o => o.HasValue).Select(o => o.Value))
                    .RunWith(Sink.Queue<int>(), _materializer);

                expected.ForEach(v =>
                {
                    queue.PullAsync().PipeTo(TestActor);
                    ExpectMsg(v);
                });
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_allow_to_have_only_one_future_waiting_for_result_in_each_point_in_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();
                var future = queue.PullAsync();
                var future2 = queue.PullAsync();
                future2.Invoking(t => t.Wait(RemainingOrDefault)).ShouldThrow<IllegalStateException>();

                sub.SendNext(1);
                future.PipeTo(TestActor);
                ExpectMsg(new Option<int>(1));

                sub.SendComplete();
                queue.PullAsync();
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_wait_for_next_element_from_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();

                queue.PullAsync().PipeTo(TestActor);
                ExpectNoMsg(_pause);

                sub.SendNext(1);
                ExpectMsg(new Option<int>(1));
                sub.SendComplete();
                queue.PullAsync();
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_fail_future_on_stream_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();

                queue.PullAsync().PipeTo(TestActor);
                ExpectNoMsg(_pause);

                sub.SendError(TestException());
                ExpectMsg<Status.Failure>(
                    f => f.Cause is AggregateException && f.Cause.InnerException.Equals(TestException()));
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_fail_future_when_stream_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();

                sub.SendError(TestException());
                queue.Invoking(q => q.PullAsync().Wait(RemainingOrDefault))
                    .ShouldThrow<TestException>();
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_timeout_future_when_stream_cannot_provide_data()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();

                queue.PullAsync().PipeTo(TestActor);
                ExpectNoMsg(_pause);

                sub.SendNext(1);
                ExpectMsg(new Option<int>(1));
                sub.SendComplete();
                queue.PullAsync();
            }, _materializer);
        }

        [Fact(Skip = "Racy, see https://github.com/akkadotnet/akka.net/pull/4424#issuecomment-632284459")]
        public void QueueSink_should_fail_pull_future_when_stream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(Sink.Queue<int>(), _materializer);
                var sub = probe.ExpectSubscription();

                queue.PullAsync().PipeTo(TestActor);
                sub.SendNext(1);
                ExpectMsg(new Option<int>(1));

                sub.SendComplete();
                var result = queue.PullAsync().Result;
                result.Should().Be(Option<int>.None);

                ((Task)queue.PullAsync()).ContinueWith(t =>
                {
                    t.Exception.InnerException.Should().BeOfType<IllegalStateException>();
                }, TaskContinuationOptions.OnlyOnFaulted).Wait();
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_keep_on_sending_even_after_the_buffer_has_been_full()
        {
            this.AssertAllStagesStopped(() =>
            {
                const int bufferSize = 16;
                const int streamElementCount = bufferSize + 4;
                var sink = Sink.Queue<int>().WithAttributes(Attributes.CreateInputBuffer(bufferSize, bufferSize));
                var tuple = Source.From(Enumerable.Range(1, streamElementCount))
                    .AlsoToMaterialized(
                        Flow.Create<int>().Take(bufferSize).WatchTermination(Keep.Right).To(Sink.Ignore<int>()),
                        Keep.Right)
                    .ToMaterialized(sink, Keep.Both)
                    .Run(_materializer);
                var probe = tuple.Item1;
                var queue = tuple.Item2;
                probe.Wait(TimeSpan.FromMilliseconds(300)).Should().BeTrue();

                for (var i = 1; i <= streamElementCount; i++)
                {
                    queue.PullAsync().PipeTo(TestActor);
                    ExpectMsg(new Option<int>(i));
                }
                queue.PullAsync().PipeTo(TestActor);
                ExpectMsg(Option<int>.None);
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_work_with_one_element_buffer()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sink = Sink.Queue<int>().WithAttributes(Attributes.CreateInputBuffer(1, 1));
                var probe = this.CreateManualPublisherProbe<int>();
                var queue = Source.FromPublisher(probe).RunWith(sink, _materializer);
                var sub = probe.ExpectSubscription();

                queue.PullAsync().PipeTo(TestActor);
                sub.SendNext(1); // should pull next element
                ExpectMsg(new Option<int>(1));

                queue.PullAsync().PipeTo(TestActor);
                ExpectNoMsg(); // element requested but buffer empty
                sub.SendNext(2);
                ExpectMsg(new Option<int>(2));

                sub.SendComplete();
                var future = queue.PullAsync();
                future.Wait(_pause).Should().BeTrue();
                future.Result.Should().Be(Option<int>.None);
            }, _materializer);
        }

        [Fact]
        public void QueueSink_should_fail_to_materialize_with_zero_sized_input_buffer()
        {
            Source.Single(1)
                .Invoking(
                    s => s.RunWith(Sink.Queue<int>().WithAttributes(Attributes.CreateInputBuffer(0, 0)), _materializer))
                .ShouldThrow<ArgumentException>();
        }
    }
}
