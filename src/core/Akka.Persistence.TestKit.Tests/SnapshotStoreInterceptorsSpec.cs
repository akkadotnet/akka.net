//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreInterceptorsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Xunit;

    public class SnapshotStoreInterceptorsSpec
    {
        [Fact]
        public void noop_must_do_nothing()
            => SnapshotStoreInterceptors.Noop.Instance
                .Awaiting(x => x.InterceptAsync(null, null))
                .ShouldNotThrow();

        [Fact]
        public void failure_must_always_throw_exception()
            => SnapshotStoreInterceptors.Failure.Instance
                .Awaiting(x => x.InterceptAsync(null, null))
                .ShouldThrowExactly<TestSnapshotStoreFailureException>();

        [Fact]
        public async Task delay_must_call_next_interceptor_after_specified_delay()
        {
            var duration = TimeSpan.FromMilliseconds(100);
            var probe = new InterceptorProbe();
            var delay = new SnapshotStoreInterceptors.Delay(duration, probe);

            var startedAt = DateTime.Now;
            await delay.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
            probe.CalledAt.Should().BeOnOrAfter(startedAt + duration);
        }

        [Fact]
        public async Task on_condition_must_accept_sync_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, __) => true, probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_accept_async_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, __) => Task.FromResult(true), probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_call_next_interceptor_unless_predicate_returns_false()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, __) => false, probe);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeFalse();
        }

        [Fact]
        public async Task on_condition_with_negation_must_call_next_interceptor_unless_predicate_returns_true()
        {
            var probe = new InterceptorProbe();
            var onCondition = new SnapshotStoreInterceptors.OnCondition((_, __) => false, probe, negate: true);

            await onCondition.InterceptAsync(null, null);

            probe.WasCalled.Should().BeTrue();
        }

        public class InterceptorProbe : ISnapshotStoreInterceptor
        {
            public bool WasCalled { get; private set; }
            public DateTime CalledAt { get; private set; }
            public string PersistenceId { get; private set; }
            public SnapshotSelectionCriteria Criteria { get; private set; }

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                CalledAt = DateTime.Now;
                WasCalled = true;
                PersistenceId = persistenceId;
                Criteria = criteria;

                return Task.CompletedTask;
            }
        }
    }
}
