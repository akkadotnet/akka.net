//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSaveBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    using InterceptorPredicate = System.Func<string, SnapshotSelectionCriteria, bool>;
    using AsyncInterceptorPredicate = System.Func<string, SnapshotSelectionCriteria, System.Threading.Tasks.Task<bool>>;

    public class SnapshotStoreSaveBehavior
    {
        public SnapshotStoreSaveBehavior(ISnapshotStoreBehaviorSetter setter)
        {
            Setter = setter;
        }

        protected readonly ISnapshotStoreBehaviorSetter Setter;

        public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor) => Setter.SetInterceptorAsync(interceptor);

        public Task Pass() => SetInterceptorAsync(SnapshotStoreInterceptors.Noop.Instance);

        public Task PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Noop.Instance));
        }

        public Task Fail() => SetInterceptorAsync(SnapshotStoreInterceptors.Failure.Instance);

        public Task FailIf(InterceptorPredicate predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance));
        }

        public Task FailIf(AsyncInterceptorPredicate predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance));
        }

        public Task FailUnless(InterceptorPredicate predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance, negate: true));
        }

        public Task FailUnless(AsyncInterceptorPredicate predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance, negate: true));
        }

        public Task FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance));
        }

        public Task FailIfWithDelay(TimeSpan delay, AsyncInterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance)
            ));
        }

        public Task FailIfWithDelay(TimeSpan delay, InterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance)
            ));
        }

        public Task FailUnlessWithDelay(TimeSpan delay, InterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance),
                negate: true
            ));
        }

        public Task FailUnlessWithDelay(TimeSpan delay, AsyncInterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            
            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance),
                negate: true
            ));
        }
    }
}