//-----------------------------------------------------------------------
// <copyright file="AsyncEnumerableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Akka.TestKit.Extensions;
using Akka.Util;
using FluentAssertions.Extensions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
#if !NETFRAMEWORK // disabling this causes .NET Framework 4.7.2 builds to fail on Linux
    public class AsyncEnumerableSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }
        private ITestOutputHelper _helper;

        public AsyncEnumerableSpec(ITestOutputHelper helper) : base(
            AkkaSpecConfig.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
            helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }


        [Fact]
        public async Task RunAsAsyncEnumerable_Uses_CancellationToken()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var input = Enumerable.Range(1, 6).ToList();

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);
                await Awaiting(async () =>
                {
                    await foreach (var a in asyncEnumerable.WithCancellation(token))
                    {
                        cts.Cancel();
                    }
                }).Should().ThrowAsync<OperationCanceledException>().WaitAsync(3.Seconds());
            }, Materializer);
        }

        [Fact]
        public async Task RunAsAsyncEnumerable_must_return_an_IAsyncEnumerableT_from_a_Source()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var input = Enumerable.Range(1, 6).ToList();
                var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);
                var output = await asyncEnumerable.ToListAsync().AsTask().WaitAsync(3.Seconds());
                output.Should().BeEquivalentTo(input, options => options.WithStrictOrdering());
            }, Materializer);
        }

        [Fact]
        public async Task RunAsAsyncEnumerable_must_allow_multiple_enumerations()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var input = Enumerable.Range(1, 6).ToList();
                var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);

                var output = await asyncEnumerable.ToListAsync().AsTask().WaitAsync(3.Seconds());
                output.Should().BeEquivalentTo(input, options => options.WithStrictOrdering());

                output = await asyncEnumerable.ToListAsync().AsTask().WaitAsync(3.Seconds());
                output.Should().BeEquivalentTo(input, options => options.WithStrictOrdering());
            }, Materializer);
        }


        [Fact]
        public async Task RunAsAsyncEnumerable_Throws_on_Abrupt_Stream_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<int>();
            var asyncEnumerable = Source.FromPublisher(probe).RunAsAsyncEnumerable(materializer);

            // Start iterating - this will call PullAsync() which sends a Request to the publisher
            var iterationTask = Task.Run(async () =>
            {
                await foreach (var _ in asyncEnumerable)
                {
                    // We won't receive any elements because we fail the stream
                }
            });

            // Wait for the async enumerator to request an element - this means PullAsync() is waiting
            await probe.ExpectRequestAsync();

            // Send an error to terminate the stream - this simulates abrupt termination
            // and is deterministic: the pending PullAsync() will receive this error
            await probe.SendErrorAsync(new TestException("Abrupt stream termination"));

            // The iteration should throw when the pending PullAsync() fails
            Exception? caughtException = null;
            try
            {
                await iterationTask.WaitAsync(10.Seconds());
            }
            catch (TestException ex)
            {
                caughtException = ex;
            }

            caughtException.Should().NotBeNull(
                "Expected TestException when stream is terminated during iteration");

            // Clean up the materializer
            materializer.Shutdown();
        }

        [Fact]
        public async Task RunAsAsyncEnumerable_Throws_if_materializer_gone_before_Enumeration()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<int>();
            var task = Source.FromPublisher(probe).RunAsAsyncEnumerable(materializer);
            materializer.Shutdown();

            await Awaiting(async () =>
            {
                await foreach (var a in task)
                {
                }
            }).Should().ThrowAsync<IllegalStateException>().WaitAsync(3.Seconds());
        }

        [Fact]
        public async Task
            AsyncEnumerableSource_Must_Complete_Immediately_With_No_elements_When_An_Empty_IAsyncEnumerable_Is_Passed_In()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                IAsyncEnumerable<int> Range() => RangeAsync(0, 0);
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(Range)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(100);
                await subscriber.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Process_All_Elements()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                IAsyncEnumerable<int> Range() => RangeAsync(0, 100);
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(Range)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(101);

                await subscriber.ExpectNextNAsync(Enumerable.Range(0, 100));
            
                await subscriber.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Process_Source_That_Immediately_Throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                IAsyncEnumerable<int> Range() => ThrowingRangeAsync(0, 100, 50);
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(Range)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(101);

                await subscriber.ExpectNextNAsync(Enumerable.Range(0, 50));

                var exception = await subscriber.ExpectErrorAsync();
            
                // Exception should be automatically unrolled, this SHOULD NOT be AggregateException
                exception.Should().BeOfType<TestException>();
                exception.Message.Should().Be("BOOM!");
            }, Materializer);
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Cancel_Running_Source_If_Downstream_Completes()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var latch = new AtomicBoolean();
                IAsyncEnumerable<int> Range() => ProbeableRangeAsync(0, 100, latch);
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(Range)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(50);
                await subscriber.ExpectNextNAsync(Enumerable.Range(0, 50));
                subscription.Cancel();

                // The cancellation token inside the IAsyncEnumerable should be cancelled
                await WithinAsync(3.Seconds(), async () =>
                {
                    await Task.Yield();
                    return latch.Value;
                });
            }, Materializer);
        }

        /// <summary>
        /// Reproduction for https://github.com/akkadotnet/akka.net/issues/6280
        /// </summary>
        [Fact]
        public async Task AsyncEnumerableSource_BugFix6280()
        {
            async IAsyncEnumerable<int> GenerateInts()
            {
                foreach (var i in Enumerable.Range(0, 100))
                {
                    if (i > 50)
                        await Task.Delay(1000);
                    yield return i;
                }
            }

            var source = Source.From(GenerateInts);
            var subscriber = this.CreateManualSubscriberProbe<int>();

            await EventFilter.Warning().ExpectAsync(0, async () =>
            {
                var mat = source
                    .WatchTermination(Keep.Right)
                    .ToMaterialized(Sink.FromSubscriber(subscriber), Keep.Left);

#pragma warning disable CS4014
                var task = mat.Run(Materializer);
#pragma warning restore CS4014

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(50);
                subscriber.ExpectNextN(Enumerable.Range(0, 50));
                subscription.Request(10); // the iterator is going to start delaying 1000ms per item here
                subscription.Cancel();


                // The cancellation token inside the IAsyncEnumerable should be cancelled
                await task;
            });
        }

        /// <summary>
        /// Reproduction for https://github.com/akkadotnet/akka.net/issues/6903
        /// </summary>
        [Fact(DisplayName = "AsyncEnumerable Source should dispose underlying async enumerator on kill switch signal")]
        public async Task AsyncEnumerableSource_Disposes_On_KillSwitch()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();
                var enumerable = new TestAsyncEnumerable(500.Milliseconds());
                var src = Source.From(() => enumerable)
                    .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                    .ToMaterialized(Sink.ActorRefWithAck<int>(probe, "init", "ack", "complete"), Keep.Left);
                var killSwitch = src.Run(Materializer);
                
                // assert init was sent
                await probe.ExpectMsgAsync<string>(msg => msg == "init");
                probe.Sender.Tell("ack");
                
                // assert enumerator is working
                foreach (var i in Enumerable.Range(0, 5))
                {
                    await probe.ExpectMsgAsync<int>(msg => msg == i);
                    probe.Sender.Tell("ack");
                }
                
                // last message was not ack-ed
                await probe.ExpectMsgAsync<int>(msg => msg == 5);
                
                killSwitch.Shutdown();
                
                // assert that enumerable resource was disposed
                await AwaitConditionAsync(() => enumerable.Disposed);
            }, Materializer);
        }
        
        [Fact(DisplayName = "AsyncEnumerable Source should dispose underlying async enumerator on kill switch signal even after ActorSystem termination")]
        public async Task AsyncEnumerableSource_Disposes_On_KillSwitch2()
        {
            var probe = CreateTestProbe();
            // A long disposing enumerable source
            var enumerable = new TestAsyncEnumerable(2.Seconds());
            var src = Source.From(() => enumerable)
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .ToMaterialized(Sink.ActorRefWithAck<int>(probe, "init", "ack", "complete"), Keep.Left);
            var killSwitch = src.Run(Materializer);
                
            // assert init was sent
            await probe.ExpectMsgAsync<string>(msg => msg == "init");
            probe.Sender.Tell("ack");
                
            // assert enumerator is working
            foreach (var i in Enumerable.Range(0, 5))
            {
                await probe.ExpectMsgAsync<int>(msg => msg == i);
                probe.Sender.Tell("ack");
            }
                
            // last message was not ack-ed
            await probe.ExpectMsgAsync<int>(msg => msg == 5);
                
            killSwitch.Shutdown();

            await Sys.Terminate();

            // enumerable was not disposed even after system termination
            enumerable.Disposed.Should().BeFalse();
            
            // assert that enumerable resource can still be disposed even after system termination
            // (Not guaranteed if process was already killed)
            await AwaitConditionAsync(() => enumerable.Disposed);
        }

        /// <summary>
        /// Reproduction for https://github.com/akkadotnet/akka.net/issues/7381
        /// </summary>
        [Fact(DisplayName = "AsyncEnumerable Source should not dispose underlying async enumerator while MoveNextAsync is in flight")]
        public async Task AsyncEnumerableSource_Does_Not_Dispose_Enumerator_While_MoveNextAsync_Is_In_Flight()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var enumerable = new BlockingMoveAsyncEnumerable();
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(() => enumerable)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(1);
                await enumerable.MoveStarted.WaitAsync(3.Seconds());

                subscription.Cancel();

                try
                {
                    var earlyViolation = await Task.WhenAny(
                        enumerable.DisposeWhileMoveInFlight,
                        Task.Delay(300.Milliseconds()));

                    ReferenceEquals(earlyViolation, enumerable.DisposeWhileMoveInFlight).Should().BeFalse(
                        "DisposeAsync must not be invoked until the in-flight MoveNextAsync has completed");
                }
                finally
                {
                    enumerable.CompleteMove();
                }

                await enumerable.Disposed.WaitAsync(3.Seconds());
                enumerable.DisposeWhileMoveInFlight.IsCompleted.Should().BeFalse(
                    "DisposeAsync must only run after MoveNextAsync has completed");
            }, Materializer);
        }

        [Fact(DisplayName = "AsyncEnumerable Source should not dispose cancellation token source while MoveNextAsync is in flight")]
        public async Task AsyncEnumerableSource_Does_Not_Dispose_CancellationTokenSource_While_MoveNextAsync_Is_In_Flight()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var enumerable = new RegisterAfterCancelAsyncEnumerable();
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.From(() => enumerable)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(1);
                await enumerable.MoveStarted.WaitAsync(3.Seconds());

                subscription.Cancel();
                await enumerable.CancellationObserved.WaitAsync(3.Seconds());

                enumerable.CompleteMove();

                await enumerable.MoveCompleted.WaitAsync(3.Seconds());
                await enumerable.Disposed.WaitAsync(3.Seconds());
                enumerable.RegisterAfterCancelFailed.IsCompleted.Should().BeFalse(
                    "the stage-owned CancellationTokenSource must stay alive until MoveNextAsync has completed");
            }, Materializer);
        }

        private class TestAsyncEnumerable: IAsyncEnumerable<int>
        {
            private readonly AsyncEnumerator _enumerator;

            public bool Disposed => _enumerator.Disposed;

            public TestAsyncEnumerable(TimeSpan shutdownDelay)
            {
                _enumerator = new AsyncEnumerator(shutdownDelay);
            }
            
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
            {
                token.ThrowIfCancellationRequested();
                return _enumerator;
            }

            private sealed class AsyncEnumerator: IAsyncEnumerator<int>
            {
                private readonly TimeSpan _shutdownDelay;
                private int _current = -1;

                public AsyncEnumerator(TimeSpan shutdownDelay)
                {
                    _shutdownDelay = shutdownDelay;
                }

                public bool Disposed { get; private set; }
                
                public async ValueTask DisposeAsync()
                {
                    await Task.Delay(_shutdownDelay);
                    Disposed = true;
                }

                public async ValueTask<bool> MoveNextAsync()
                {
                    await Task.Delay(100);
                    _current++;
                    return true;
                }

                public int Current
                {
                    get
                    {
                        if (_current == -1)
                            throw new IndexOutOfRangeException("MoveNextAsync has not been called");
                        if (Disposed)
                            throw new ObjectDisposedException("Enumerator already disposed");
                        return _current;
                    }
                }
            }
        }

        private sealed class BlockingMoveAsyncEnumerable : IAsyncEnumerable<int>
        {
            private readonly BlockingMoveAsyncEnumerator _enumerator = new();

            public Task<NotUsed> MoveStarted => _enumerator.MoveStarted.Task;
            public Task<NotUsed> DisposeWhileMoveInFlight => _enumerator.DisposeWhileMoveInFlight.Task;
            public Task<NotUsed> Disposed => _enumerator.Disposed.Task;

            public void CompleteMove()
                => _enumerator.AllowMoveToComplete.TrySetResult(NotUsed.Instance);

            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
                => _enumerator;

            private sealed class BlockingMoveAsyncEnumerator : IAsyncEnumerator<int>
            {
                private int _moveInFlight;

                public readonly TaskCompletionSource<NotUsed> AllowMoveToComplete =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> MoveStarted =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> DisposeWhileMoveInFlight =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> Disposed =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public async ValueTask<bool> MoveNextAsync()
                {
                    Interlocked.Exchange(ref _moveInFlight, 1);
                    MoveStarted.TrySetResult(NotUsed.Instance);

                    try
                    {
                        await AllowMoveToComplete.Task.ConfigureAwait(false);
                        Current = 1;
                        return true;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _moveInFlight, 0);
                    }
                }

                public ValueTask DisposeAsync()
                {
                    if (Volatile.Read(ref _moveInFlight) == 1)
                        DisposeWhileMoveInFlight.TrySetResult(NotUsed.Instance);

                    Disposed.TrySetResult(NotUsed.Instance);
                    return new ValueTask();
                }

                public int Current { get; private set; }
            }
        }

        private sealed class RegisterAfterCancelAsyncEnumerable : IAsyncEnumerable<int>
        {
            private readonly RegisterAfterCancelAsyncEnumerator _enumerator = new();

            public Task<NotUsed> MoveStarted => _enumerator.MoveStarted.Task;
            public Task<NotUsed> CancellationObserved => _enumerator.CancellationObserved.Task;
            public Task<NotUsed> MoveCompleted => _enumerator.MoveCompleted.Task;
            public Task<NotUsed> RegisterAfterCancelFailed => _enumerator.RegisterAfterCancelFailed.Task;
            public Task<NotUsed> Disposed => _enumerator.Disposed.Task;

            public void CompleteMove()
                => _enumerator.AllowMoveToComplete.TrySetResult(NotUsed.Instance);

            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
            {
                _enumerator.Token = token;
                return _enumerator;
            }

            private sealed class RegisterAfterCancelAsyncEnumerator : IAsyncEnumerator<int>
            {
                public CancellationToken Token { private get; set; }

                public readonly TaskCompletionSource<NotUsed> AllowMoveToComplete =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> MoveStarted =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> CancellationObserved =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> MoveCompleted =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> RegisterAfterCancelFailed =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public readonly TaskCompletionSource<NotUsed> Disposed =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public async ValueTask<bool> MoveNextAsync()
                {
                    using var cancellationRegistration = Token.Register(
                        () => CancellationObserved.TrySetResult(NotUsed.Instance));

                    MoveStarted.TrySetResult(NotUsed.Instance);

                    await AllowMoveToComplete.Task.ConfigureAwait(false);

                    try
                    {
                        _ = Token.WaitHandle;
                    }
                    catch (ObjectDisposedException)
                    {
                        RegisterAfterCancelFailed.TrySetResult(NotUsed.Instance);
                    }

                    Current = 1;
                    MoveCompleted.TrySetResult(NotUsed.Instance);
                    return true;
                }

                public ValueTask DisposeAsync()
                {
                    Disposed.TrySetResult(NotUsed.Instance);
                    return new ValueTask();
                }

                public int Current { get; private set; }
            }
        }

        [Fact]
        public async Task AsyncEnumerableSource_Disposes_OnCancel()
        {
            var resource = new Resource();
            var tcs = new System.Threading.Tasks.TaskCompletionSource<NotUsed>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            var src = Source.From(() =>
                CancelTestGenerator(tcs, resource, default));
            src.To(Sink.Ignore<int>()).Run(Materializer);
            await tcs.Task;
            Materializer.Shutdown();
            await Task.Delay(500);
            Assert.False(resource.IsActive);
        }

        private static async IAsyncEnumerable<int> RangeAsync(int start, int count, 
            [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (var i in Enumerable.Range(start, count))
            {
                await Task.Delay(10, token);
                if(token.IsCancellationRequested)
                    yield break;
                yield return i;
            }
        }
        
        private static async IAsyncEnumerable<int> ThrowingRangeAsync(int start, int count, int throwAt,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (var i in Enumerable.Range(start, count))
            {
                await Task.Yield();
                if(token.IsCancellationRequested)
                    yield break;

                if (i == throwAt)
                    throw new TestException("BOOM!");
                
                yield return i;
            }
        }
        
        private static async IAsyncEnumerable<int> ProbeableRangeAsync(int start, int count, AtomicBoolean latch,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            token.Register(() => { latch.GetAndSet(true); });
            foreach (var i in Enumerable.Range(start, count))
            {
                await Task.Yield();
                if(token.IsCancellationRequested)
                    yield break;

                yield return i;
            }
        }

        public static async IAsyncEnumerable<int> CancelTestGenerator(
            TaskCompletionSource<NotUsed> tcs,
            Resource resource,
            [EnumeratorCancellation] CancellationToken token
        )
        {
            await using var res = resource;
            int i = 0;
            bool isSet = false;
            while (true)
            {
                await Task.Delay(1, token).ConfigureAwait(false);
                yield return i++;
                if (isSet == false)
                {
                    tcs.TrySetResult(NotUsed.Instance);
                    isSet = true;
                }
            }
            // ReSharper disable once IteratorNeverReturns
        }

        public class Resource : IAsyncDisposable
        {
            public bool IsActive = true;
            public ValueTask DisposeAsync()
            {
                IsActive = false;
                Console.WriteLine("Enumerator completed and resource disposed");
                return new ValueTask();
            }
        }
    }
#endif
}
