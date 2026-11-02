//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreInterceptorsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using FluentAssertions.Extensions;
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Persistence.TestKit.Tests
{
    public class SnapshotStoreInterceptorsSpec
    {
        [Fact]
        public async Task noop_must_do_nothing()
        {
            await Awaiting(async () =>
            {
                await SnapshotStoreInterceptors.Noop.Instance.InterceptAsync(null, null);
            }).Should().NotThrowAsync();
        }

        [Fact]
        public async Task failure_must_always_throw_exception()
        {
            await Awaiting(async () =>
            {
                await SnapshotStoreInterceptors.Failure.Instance.InterceptAsync(null, null);
            }).Should().ThrowExactlyAsync<TestSnapshotStoreFailureException>();
        }

        [Fact]
        public async Task delay_must_call_next_interceptor_after_specified_delay()
        {
            var duration = TimeSpan.FromMilliseconds(200);
            var epsilon = TimeSpan.FromMilliseconds(50);
            var probe = new InterceptorProbe();
            var delay = new SnapshotStoreInterceptors.Delay(duration, probe);

            var startedAt = DateTime.Now;
            await delay.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
            probe.CalledAt.Should().BeOnOrAfter(startedAt + duration - epsilon);
        }

        [Fact]
        public async Task cancelable_delay_must_call_next_interceptor_immediately_after_cancellation()
        {
            var totalDuration = 400.Milliseconds();
            var delayDuration = 200.Milliseconds();
            var epsilon = TimeSpan.FromMilliseconds(50);
            
            using var cts = new CancellationTokenSource();
            var synchronizationTcs = new TaskCompletionSource<bool>();
            
            // Custom interceptor that signals when it's called
            var probe = new InterceptorProbe();
            probe.InterceptAsyncFunc = (persistenceId, criteria) =>
            {
                synchronizationTcs.TrySetResult(true);
                return Task.CompletedTask;
            };
            
            var delay = new SnapshotStoreInterceptors.CancelableDelay(totalDuration, probe, cts.Token);

            var startedAt = DateTime.Now;
            var task = delay.InterceptAsync(null, null);
            
            // Wait less than the full delay time
            await Task.Delay(delayDuration);
            
            // Ensure the probe hasn't been called yet (not using probe.WasCalled since it might have race conditions)
            synchronizationTcs.Task.IsCompleted.Should().BeFalse();
            
            // Cancel the delay
            cts.Cancel();
            
            // Wait for the probe to be called
            await synchronizationTcs.Task;
            
            // Now we can safely check that the probe was called
            probe.WasCalled.Should().BeTrue();
            probe.CalledAt.Should().BeOnOrAfter(startedAt + delayDuration - epsilon);
            
            // Wait for the original task to complete
            await task;
        }
        
        [Fact]
        public async Task on_condition_must_accept_sync_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, _) => true, probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_accept_async_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, _) => Task.FromResult(true), probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_call_next_interceptor_unless_predicate_returns_false()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, _) => false, probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeFalse();
        }

        [Fact]
        public async Task on_condition_with_negation_must_call_next_interceptor_unless_predicate_returns_true()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, _) => false, probe, negate: true);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        public class InterceptorProbe : ISnapshotStoreInterceptor
        {
            public bool WasCalled { get; private set; }
            public DateTime CalledAt { get; private set; }
            public string PersistenceId { get; private set; }
            public SnapshotSelectionCriteria Criteria { get; private set; }
            public Func<string, SnapshotSelectionCriteria, Task> InterceptAsyncFunc { get; set; }

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                CalledAt = DateTime.Now;
                WasCalled = true;
                PersistenceId = persistenceId;
                Criteria = criteria;

                if (InterceptAsyncFunc != null)
                    return InterceptAsyncFunc(persistenceId, criteria);
            
                return Task.CompletedTask;
            }
        }
    }
}
