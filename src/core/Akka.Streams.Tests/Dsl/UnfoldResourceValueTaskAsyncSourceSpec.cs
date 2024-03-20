// -----------------------------------------------------------------------
//  <copyright file="UnfoldResourceValueTaskAsyncSourceSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.Tests.Util;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl;

public class UnfoldResourceValueTaskAsyncSourceSpec : AkkaSpec
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

    public UnfoldResourceValueTaskAsyncSourceSpec(ITestOutputHelper helper)
        : base(Utils.UnboundedMailboxConfig, helper)
    {
        Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
        var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
        Materializer = Sys.Materializer(settings);
    }

    public ActorMaterializer Materializer { get; }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_unfold_data_from_a_resource()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var createPromise = new TaskCompletionSource<Done>();
            var closePromise = new TaskCompletionSource<Done>();

            var values = Enumerable.Range(0, 1000).ToList();
            var resource = new ResourceDummy<int>(values, createPromise.Task, closeFuture: closePromise.Task);

            var probe = this.CreateSubscriberProbe<int>();
            Source.UnfoldResourceValueTaskAsync(
                    () => resource.Create().ToValueTask(),
                    r => r.Read().ToValueTask(),
                    close: r => r.Close().ToValueTask())
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
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_resource_successfully_right_after_open()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<int>();
            var firtRead = new TaskCompletionSource<Done>();
            var resource = new ResourceDummy<int>(new[] { 1 }, firstReadFuture: firtRead.Task);

            Source.UnfoldResourceValueTaskAsync(
                    create: () => resource.Create().ToValueTask(),
                    read: reader => reader.Read().ToValueTask(),
                    close: reader => reader.Close().ToValueTask())
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
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_create_throws_exception()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<Task>();
            var testException = new TestException("create failed");

            Source.UnfoldResourceValueTaskAsync<Task, Task>(
                    create: () => throw testException,
                    read: _ => default,
                    close: _ => default)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.EnsureSubscriptionAsync();
            (await probe.ExpectErrorAsync()).ShouldBe(testException);
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_create_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<Task>();
            var testException = new TestException("create failed");

            Source.UnfoldResourceValueTaskAsync<Task, Task>(
                    create: () => Task.FromException<Task>(testException).ToValueTask(),
                    read: _ => default,
                    close: _ => default)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.EnsureSubscriptionAsync();
            (await probe.ExpectErrorAsync()).ShouldBe(testException);
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_close_throws_exception()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<Task>();
            var testException = new TestException("");

            Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(Task.CompletedTask).ToValueTask(),
                    _ => Task.FromResult(Option<Task>.None).ToValueTask(),
                    _ => throw testException)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.EnsureSubscriptionAsync();
            await probe.RequestAsync(1L);
            await probe.ExpectErrorAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_close_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<Task>();
            var testException = new TestException("create failed");

            Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(Task.CompletedTask).ToValueTask(),
                    _ => Task.FromResult(Option<Task>.None).ToValueTask(),
                    _ => Task.FromException<Done>(testException).ToValueTask())
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.EnsureSubscriptionAsync();
            await probe.RequestAsync(1L);
            await probe.ExpectErrorAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_continue_when_strategy_is_resume_and_read_throws()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var result = Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(new object[] { 1, 2, new TestException("read-error"), 3 }.GetEnumerator()).ToValueTask(),
                    iterator =>
                    {
                        if (iterator.MoveNext())
                        {
                            var next = iterator.Current;
                            switch (next)
                            {
                                case int n:
                                    return Task.FromResult(Option<int>.Create(n)).ToValueTask();
                                case TestException e:
                                    throw e;
                                default:
                                    throw new Exception($"Unexpected: {next}");
                            }
                        }

                        return Task.FromResult(Option<int>.None).ToValueTask();
                    },
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Seq<int>(), Materializer);

            var r = await result.ShouldCompleteWithin(3.Seconds());
            r.ShouldBe(new[] { 1, 2, 3 });
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_continue_when_strategy_is_resume_and_read_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var result = Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(new object[] { 1, 2, new TestException("read-error"), 3 }.GetEnumerator()).ToValueTask(),
                    iterator =>
                    {
                        if (iterator.MoveNext())
                        {
                            var next = iterator.Current;
                            switch (next)
                            {
                                case int n:
                                    return Task.FromResult(Option<int>.Create(n)).ToValueTask();
                                case TestException e:
                                    return Task.FromException<Option<int>>(e).ToValueTask();
                                default:
                                    throw new Exception($"Unexpected: {next}");
                            }
                        }

                        return Task.FromResult(Option<int>.None).ToValueTask();
                    },
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Seq<int>(), Materializer);

            var r = await result.ShouldCompleteWithin(3.Seconds());
            r.ShouldBe(new[] { 1, 2, 3 });
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_and_open_stream_again_when_strategy_is_restart_and_read_throws()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var failed = false;
            var startCount = new AtomicCounter(0);

            var result = Source.UnfoldResourceValueTaskAsync(
                    () =>
                    {
                        startCount.IncrementAndGet();
                        return Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask();
                    },
                    reader =>
                    {
                        if (!failed)
                        {
                            failed = true;
                            throw new TestException("read-error");
                        }

                        return reader.MoveNext() && reader.Current != null
                            ? Task.FromResult(Option<int>.Create((int)reader.Current)).ToValueTask()
                            : Task.FromResult(Option<int>.None).ToValueTask();
                    },
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.Seq<int>(), Materializer);

            var r = await result.ShouldCompleteWithin(3.Seconds());
            r.ShouldBe(new[] { 1, 2, 3 });
            startCount.Current.ShouldBe(2);
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_and_open_stream_again_when_strategy_is_restart_and_read_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var failed = false;
            var startCount = new AtomicCounter(0);

            var result = Source.UnfoldResourceValueTaskAsync(
                    () =>
                    {
                        startCount.IncrementAndGet();
                        return Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask();
                    },
                    reader =>
                    {
                        if (!failed)
                        {
                            failed = true;
                            return Task.FromException<Option<int>>(new TestException("read-error")).ToValueTask();
                        }

                        return reader.MoveNext() && reader.Current != null
                            ? Task.FromResult(Option<int>.Create((int)reader.Current)).ToValueTask()
                            : Task.FromResult(Option<int>.None).ToValueTask();
                    },
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.Seq<int>(), Materializer);

            var r = await result.ShouldCompleteWithin(3.Seconds());
            r.ShouldBe(new[] { 1, 2, 3 });
            startCount.Current.ShouldBe(2);
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_restarting_and_close_throws()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<int>();
            Source.UnfoldResourceValueTaskAsync<int, IEnumerator>(
                    () => Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask(),
                    _ => throw new TestException("read-error"),
                    _ => throw new TestException("close-error"))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.RequestAsync(1L);
            (await probe.ExpectErrorAsync()).Message.ShouldBe("close-error");
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_restarting_and_close_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<int>();
            Source.UnfoldResourceValueTaskAsync<int, IEnumerator>(
                    () => Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask(),
                    _ => throw new TestException("read-error"),
                    _ => Task.FromException<Done>(new TestException("close-error")).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.RequestAsync(1L);
            (await probe.ExpectErrorAsync()).Message.ShouldBe("close-error");
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_restarting_and_start_throws()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<int>();
            var startCounter = new AtomicCounter(0);

            Source.UnfoldResourceValueTaskAsync<int, IEnumerator>(
                    () =>
                    {
                        return startCounter.IncrementAndGet() < 2 ?
                            Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask() :
                            throw new TestException("start-error");
                    },
                    _ => throw new TestException("read-error"),
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.RequestAsync(1L);
            (await probe.ExpectErrorAsync()).Message.ShouldBe("start-error");
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_fail_when_restarting_and_start_returns_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = this.CreateSubscriberProbe<int>();
            var startCounter = new AtomicCounter(0);

            Source.UnfoldResourceValueTaskAsync<int, IEnumerator>(
                    () =>
                    {
                        return startCounter.IncrementAndGet() < 2 ?
                            Task.FromResult(new[] { 1, 2, 3 }.GetEnumerator()).ToValueTask() :
                            Task.FromException<IEnumerator>(new TestException("start-error")).ToValueTask();
                    },
                    _ => throw new TestException("read-error"),
                    _ => Task.FromResult(Done.Instance).ToValueTask())
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(probe), Materializer);

            await probe.RequestAsync(1L);
            (await probe.ExpectErrorAsync()).Message.ShouldBe("start-error");
        }, Materializer);
    }

    // Could not use AssertAllStagesStoppedAsync because materializer is shut down inside the test.
    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_resource_when_stream_is_abruptly_terminated()
    {
        var closePromise = new TaskCompletionSource<string>();
        var materializer = ActorMaterializer.Create(Sys);
        var p = Source.UnfoldResourceValueTaskAsync(
                () => Task.FromResult(closePromise).ToValueTask(),
                // a slow trickle of elements that never ends
                _ => FutureTimeoutSupport.After(TimeSpan.FromMilliseconds(100), Sys.Scheduler, () => Task.FromResult(Option<string>.Create("element"))).ToValueTask(),
                tcs =>
                {
                    tcs.SetResult("Closed");
                    return Task.FromResult(Done.Instance).ToValueTask();
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
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_resource_when_stream_is_quickly_cancelled()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var closePromise = new TaskCompletionSource<string>();
            var probe = Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(closePromise).ToValueTask(),
                    _ => Task.FromResult(Option<string>.Create("whatever")).ToValueTask(),
                    tcs =>
                    {
                        tcs.SetResult("Closed");
                        return Task.FromResult(Done.Instance).ToValueTask();
                    })
                .RunWith(this.SinkProbe<string>(), Materializer);

            await probe.CancelAsync();
                
            var r = await closePromise.Task.ShouldCompleteWithin(3.Seconds());
            r.Should().Be("Closed");
        }, Materializer);
    }

    [Fact]
    public async Task A_UnfoldResourceValueTaskAsyncSource_must_close_resource_when_stream_is_quickly_cancelled_reproducer_2()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var closePromise = new TaskCompletionSource<string>();
            Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(new[] { "a", "b", "c" }.GetEnumerator()).ToValueTask(),
                    m => Task.FromResult(m.MoveNext() && m.Current != null ? (string)m.Current : Option<string>.None).ToValueTask(),
                    _ =>
                    {
                        closePromise.SetResult("Closed");
                        return Task.FromResult(Done.Instance).ToValueTask();
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
    public async Task A_UnfoldResourceValueTaskAsyncSource_close_the_resource_when_reading_an_element_returns_a_failed_future()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var closePromise = new TaskCompletionSource<string>();
            var probe = this.CreateSubscriberProbe<Task>();

            Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(closePromise).ToValueTask(),
                    _ => Task.FromException<Option<Task>>(new TestException("read failed")).ToValueTask(),
                    tcs =>
                    {
                        tcs.TrySetResult("Closed");
                        return Task.FromResult(Done.Instance).ToValueTask();
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
    public async Task A_UnfoldResourceValueTaskAsyncSource_close_the_resource_when_reading_an_element_throws()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var closePromise = new TaskCompletionSource<string>();
            var probe = this.CreateSubscriberProbe<Task>();

            ValueTask<Option<Task>> Fail(TaskCompletionSource<string> _) => throw new TestException("read failed");

            Source.UnfoldResourceValueTaskAsync(
                    () => Task.FromResult(closePromise).ToValueTask(),
                    Fail,
                    tcs =>
                    {
                        tcs.SetResult("Closed");
                        return Task.FromResult(Done.Instance).ToValueTask();
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