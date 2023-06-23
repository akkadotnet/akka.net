//-----------------------------------------------------------------------
// <copyright file="UnfoldResourceAsyncSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions.Extensions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class UnfoldResourceAsyncSourceSpec : AkkaSpec
    {
        private class ResourceDummy<T>
        {
            private readonly IEnumerator<T> _iterator;
            private readonly Task<Done> _createFuture;
            private readonly Task<Done> _firstReadFuture;
            private readonly Task<Done> _closeFuture;

            private readonly TaskCompletionSource<Done> _createdPromise = new();
            private readonly TaskCompletionSource<Done> _closedPromise = new();
            private readonly TaskCompletionSource<Done> _firstReadPromise = new();

            // these can be used to observe when the resource calls has happened
            public Task<Done> Created => _createdPromise.Task;
            public Task<Done> FirstElementRead => _firstReadPromise.Task;
            public Task<Done> Closed => _closedPromise.Task;

            public ResourceDummy(IEnumerable<T> values, Task<Done> createFuture = default, Task<Done> firstReadFuture = default, Task<Done> closeFuture = default)
            {
                _iterator = values.GetEnumerator();
                _createFuture = createFuture ?? Task.FromResult(Done.Instance);
                _firstReadFuture = firstReadFuture ?? Task.FromResult(Done.Instance);
                _closeFuture = closeFuture ?? Task.FromResult(Done.Instance);
            }

            public Task<ResourceDummy<T>> Create()
            {
                _createdPromise.TrySetResult(Done.Instance);
                return _createFuture.ContinueWith(_ => this);
            }

            public Task<Option<T>> Read()
            {
                if (!_firstReadPromise.Task.IsCompleted)
                    _firstReadPromise.TrySetResult(Done.Instance);

                return _firstReadFuture.ContinueWith(_ => _iterator.MoveNext() ? _iterator.Current : Option<T>.None);
            }

            public Task<Done> Close()
            {
                _closedPromise.TrySetResult(Done.Instance);
                return _closeFuture;
            }
        }

        public UnfoldResourceAsyncSourceSpec(ITestOutputHelper helper)
            : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            Materializer = Sys.Materializer(settings);
        }

        public ActorMaterializer Materializer { get; }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_unfold_data_from_a_resource()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var createPromise = new TaskCompletionSource<Done>();
                var closePromise = new TaskCompletionSource<Done>();

                var values = Enumerable.Range(0, 1000).ToList();
                var resource = new ResourceDummy<int>(values, createPromise.Task, closeFuture: closePromise.Task);

                var probe = this.CreateSubscriberProbe<int>();
                Source.UnfoldResourceAsync(
                        () => resource.Create(),
                        r => r.Read(),
                        close: r => r.Close())
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1);
                await resource.Created.ShouldCompleteWithin(3.Seconds());
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                createPromise.SetResult(Done.Instance);

                foreach (var i in values)
                {
                    await resource.FirstElementRead.ShouldCompleteWithin(3.Seconds());
                    (await probe.ExpectNextAsync()).ShouldBe(i);
                    await probe.RequestAsync(1);
                }

                await resource.Closed.ShouldCompleteWithin(3.Seconds());
                closePromise.SetResult(Done.Instance);

                await probe.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_resource_successfully_right_after_open()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                var firtRead = new TaskCompletionSource<Done>();
                var resource = new ResourceDummy<int>(new[] { 1 }, firstReadFuture: firtRead.Task);

                Source.UnfoldResourceAsync(
                        create: () => resource.Create(),
                        read: reader => reader.Read(),
                        close: reader => reader.Close())
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1L);
                await resource.FirstElementRead.ShouldCompleteWithin(3.Seconds());
                // we cancel before we complete first read (racy)
                await probe.CancelAsync();
                await Task.Delay(100);
                firtRead.SetResult(Done.Instance);

                await resource.Closed.ShouldCompleteWithin(3.Seconds());
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_create_throws_exception()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<Task>();
                var testException = new TestException("create failed");

                Source.UnfoldResourceAsync<Task, Task>(
                        create: () => throw testException,
                        read: _ => default,
                        close: _ => default)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                (await probe.ExpectErrorAsync()).ShouldBe(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_create_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<Task>();
                var testException = new TestException("create failed");

                Source.UnfoldResourceAsync<Task, Task>(
                        create: () => Task.FromException<Task>(testException),
                        read: _ => default,
                        close: _ => default)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                (await probe.ExpectErrorAsync()).ShouldBe(testException);
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_close_throws_exception()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<Task>();
                var testException = new TestException("");

                Source.UnfoldResourceAsync(
                        () => Task.FromResult(Task.CompletedTask),
                        _ => Task.FromResult(Option<Task>.None),
                        _ => throw testException)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                await probe.RequestAsync(1L);
                await probe.ExpectErrorAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_close_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<Task>();
                var testException = new TestException("create failed");

                Source.UnfoldResourceAsync(
                        () => Task.FromResult(Task.CompletedTask),
                        _ => Task.FromResult(Option<Task>.None),
                        _ => Task.FromException<Done>(testException))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                await probe.RequestAsync(1L);
                await probe.ExpectErrorAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_continue_when_strategy_is_resume_and_read_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = Source.UnfoldResourceAsync(
                        () => Task.FromResult(new object[] { 1, 2, new TestException("read-error"), 3 }.GetEnumerator()),
                        iterator =>
                        {
                            if (iterator.MoveNext())
                            {
                                var next = iterator.Current;
                                switch (next)
                                {
                                    case int n:
                                        return Task.FromResult(Option<int>.Create(n));
                                    case TestException e:
                                        throw e;
                                    default:
                                        throw new Exception($"Unexpected: {next}");
                                }
                            }

                            return Task.FromResult(Option<int>.None);
                        },
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.Seq<int>(), Materializer);

                var r = await result.ShouldCompleteWithin(3.Seconds());
                r.ShouldBe(new[] { 1, 2, 3 });
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_continue_when_strategy_is_resume_and_read_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = Source.UnfoldResourceAsync(
                        () => Task.FromResult(new object[] { 1, 2, new TestException("read-error"), 3 }.GetEnumerator()),
                        iterator =>
                        {
                            if (iterator.MoveNext())
                            {
                                var next = iterator.Current;
                                switch (next)
                                {
                                    case int n:
                                        return Task.FromResult(Option<int>.Create(n));
                                    case TestException e:
                                        return Task.FromException<Option<int>>(e);
                                    default:
                                        throw new Exception($"Unexpected: {next}");
                                }
                            }

                            return Task.FromResult(Option<int>.None);
                        },
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.Seq<int>(), Materializer);

                var r = await result.ShouldCompleteWithin(3.Seconds());
                r.ShouldBe(new[] { 1, 2, 3 });
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_and_open_stream_again_when_strategy_is_restart_and_read_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failed = false;
                var startCount = new AtomicCounter(0);

                var result = Source.UnfoldResourceAsync(
                        () =>
                        {
                            startCount.IncrementAndGet();
                            return Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator());
                        },
                        reader =>
                        {
                            if (!failed)
                            {
                                failed = true;
                                throw new TestException("read-error");
                            }

                            return reader.MoveNext() && reader.Current != null
                                ? Task.FromResult(Option<int>.Create((int)reader.Current))
                                : Task.FromResult(Option<int>.None);
                        },
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.Seq<int>(), Materializer);

                var r = await result.ShouldCompleteWithin(3.Seconds());
                r.ShouldBe(new[] { 1, 2, 3 });
                startCount.Current.ShouldBe(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_and_open_stream_again_when_strategy_is_restart_and_read_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failed = false;
                var startCount = new AtomicCounter(0);

                var result = Source.UnfoldResourceAsync(
                        () =>
                        {
                            startCount.IncrementAndGet();
                            return Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator());
                        },
                        reader =>
                        {
                            if (!failed)
                            {
                                failed = true;
                                return Task.FromException<Option<int>>(new TestException("read-error"));
                            }

                            return reader.MoveNext() && reader.Current != null
                                ? Task.FromResult(Option<int>.Create((int)reader.Current))
                                : Task.FromResult(Option<int>.None);
                        },
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.Seq<int>(), Materializer);

                var r = await result.ShouldCompleteWithin(3.Seconds());
                r.ShouldBe(new[] { 1, 2, 3 });
                startCount.Current.ShouldBe(2);
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_restarting_and_close_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                Source.UnfoldResourceAsync<int, IEnumerator>(
                        () => Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()),
                        _ => throw new TestException("read-error"),
                        _ => throw new TestException("close-error"))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1L);
                (await probe.ExpectErrorAsync()).Message.ShouldBe("close-error");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_restarting_and_close_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                Source.UnfoldResourceAsync<int, IEnumerator>(
                        () => Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()),
                        _ => throw new TestException("read-error"),
                        _ => Task.FromException<Done>(new TestException("close-error")))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1L);
                (await probe.ExpectErrorAsync()).Message.ShouldBe("close-error");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_restarting_and_start_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                var startCounter = new AtomicCounter(0);

                Source.UnfoldResourceAsync<int, IEnumerator>(
                        () =>
                        {
                            return startCounter.IncrementAndGet() < 2 ?
                                Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()) :
                                throw new TestException("start-error");
                        },
                        _ => throw new TestException("read-error"),
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1L);
                (await probe.ExpectErrorAsync()).Message.ShouldBe("start-error");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_fail_when_restarting_and_start_returns_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                var startCounter = new AtomicCounter(0);

                Source.UnfoldResourceAsync<int, IEnumerator>(
                        () =>
                        {
                            return startCounter.IncrementAndGet() < 2 ?
                                Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()) :
                                Task.FromException<IEnumerator>(new TestException("start-error"));
                        },
                        _ => throw new TestException("read-error"),
                        _ => Task.FromResult(Done.Instance))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.RequestAsync(1L);
                (await probe.ExpectErrorAsync()).Message.ShouldBe("start-error");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_use_dedicated_blocking_io_dispatcher_by_default()
        {
            // use a separate materializer to ensure we know what child is our stream
            var materializer = Sys.Materializer();
            
            await this.AssertAllStagesStoppedAsync(() => {
                var tcs = new TaskCompletionSource<Task>();
                try
                {
                    var t = Source.UnfoldResourceAsync<string, Task>(
                            () => tcs.Task, // never complete
                            _ => default,
                            _ => default)
                        .RunWith(Sink.Ignore<string>(), materializer);

                    ((ActorMaterializerImpl)materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance,
                        TestActor);
                    var @ref = ExpectMsg<StreamSupervisor.Children>().Refs
                        .Single(c => c.Path.ToString().EndsWith("unfoldResourceSourceAsync"));
                    Utils.AssertDispatcher(@ref, ActorAttributes.IODispatcher.Name);
                }
                finally
                {
                    tcs.TrySetResult(Task.CompletedTask);
                }

                return Task.CompletedTask;
            }, materializer);
        }

        // Could not use AssertAllStagesStoppedAsync because materializer is shut down inside the test.
        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_resource_when_stream_is_abruptly_terminated()
        {
            var closePromise = new TaskCompletionSource<string>();
            var materializer = ActorMaterializer.Create(Sys);
            var p = Source.UnfoldResourceAsync(
                    () => Task.FromResult(closePromise),
                    // a slow trickle of elements that never ends
                    _ => FutureTimeoutSupport.After(TimeSpan.FromMilliseconds(100), Sys.Scheduler, () => Task.FromResult(Option<string>.Create("element"))),
                    tcs =>
                    {
                        tcs.SetResult("Closed");
                        return Task.FromResult(Done.Instance);
                    })
                .RunWith(Sink.AsPublisher<string>(false), materializer);

            var c = this.CreateManualSubscriberProbe<string>();
            p.Subscribe(c);
            await c.ExpectSubscriptionAsync();
            
            materializer.Shutdown();
            materializer.IsShutdown.Should().BeTrue();
            
            var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
            r.Should().Be("Closed");
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_resource_when_stream_is_quickly_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var closePromise = new TaskCompletionSource<string>();
                var probe = Source.UnfoldResourceAsync(
                        () => Task.FromResult(closePromise),
                        _ => Task.FromResult(Option<string>.Create("whatever")),
                        tcs =>
                        {
                            tcs.SetResult("Closed");
                            return Task.FromResult(Done.Instance);
                        })
                    .RunWith(this.SinkProbe<string>(), Materializer);

                await probe.CancelAsync();
                
                var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
                r.Should().Be("Closed");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_must_close_resource_when_stream_is_quickly_cancelled_reproducer_2()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var closePromise = new TaskCompletionSource<string>();
                Source.UnfoldResourceAsync(
                        () => Task.FromResult(new[] { "a", "b", "c" }.GetEnumerator()),
                        m => Task.FromResult(m.MoveNext() && m.Current != null ? (string)m.Current : Option<string>.None),
                        _ =>
                        {
                            closePromise.SetResult("Closed");
                            return Task.FromResult(Done.Instance);
                        })
                    .Select(m =>
                    {
                        Output.WriteLine($"Elem=> {m}");
                        return m;
                    })
                    .RunWith(Sink.Cancelled<string>(), Materializer);

                var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
                r.Should().Be("Closed");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_close_the_resource_when_reading_an_element_returns_a_failed_future()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var closePromise = new TaskCompletionSource<string>();
                var probe = this.CreateSubscriberProbe<Task>();

                Source.UnfoldResourceAsync(
                        () => Task.FromResult(closePromise),
                        _ => Task.FromException<Option<Task>>(new TestException("read failed")),
                        tcs =>
                        {
                            tcs.TrySetResult("Closed");
                            return Task.FromResult(Done.Instance);
                        })
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                await probe.RequestAsync(1L);
                await probe.ExpectErrorAsync();

                var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
                r.Should().Be("Closed");
            }, Materializer);
        }

        [Fact]
        public async Task A_UnfoldResourceAsyncSource_close_the_resource_when_reading_an_element_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var closePromise = new TaskCompletionSource<string>();
                var probe = this.CreateSubscriberProbe<Task>();

                Task<Option<Task>> Fail(TaskCompletionSource<string> _) => throw new TestException("read failed");

                Source.UnfoldResourceAsync(
                        () => Task.FromResult(closePromise),
                        Fail,
                        tcs =>
                        {
                            tcs.SetResult("Closed");
                            return Task.FromResult(Done.Instance);
                        })
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                await probe.RequestAsync(1L);
                await probe.ExpectErrorAsync();

                var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
                r.Should().Be("Closed");
            }, Materializer);
        }
    }
}
