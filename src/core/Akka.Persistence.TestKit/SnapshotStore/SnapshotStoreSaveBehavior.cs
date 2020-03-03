//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSaveBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    using InterceptorPredicate = System.Func<string, SnapshotSelectionCriteria, bool>;
    using AsyncInterceptorPredicate = System.Func<string, SnapshotSelectionCriteria, System.Threading.Tasks.Task<bool>>;

    /// <summary>
    ///     Built-in snapshot store interceptors who will alter message Write of <see cref="TestSnapshotStore"/>.
    /// </summary>
    public class SnapshotStoreSaveBehavior
    {
        public SnapshotStoreSaveBehavior(ISnapshotStoreBehaviorSetter setter)
        {
            Setter = setter;
        }

        protected readonly ISnapshotStoreBehaviorSetter Setter;

        /// <summary>
        ///     Use custom, user defined interceptor.
        /// </summary>
        /// <param name="interceptor">User defined interceptor which implements <see cref="ISnapshotStoreInterceptor"/> interface.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="interceptor"/> is <c>null</c>.</exception>
        public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor)
        {
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));

            return Setter.SetInterceptorAsync(interceptor);
        }

        /// <summary>
        ///     Pass all messages to snapshot store without interfering.
        /// </summary>
        /// <remarks>
        ///     By using this interceptor <see cref="TestSnapshotStore"/> all snapshot store operations will work like
        ///     in standard <see cref="Akka.Persistence.Snapshot.MemorySnapshotStore"/>.
        /// </remarks>
        public Task Pass() => SetInterceptorAsync(SnapshotStoreInterceptors.Noop.Instance);

        /// <summary>
        ///     Delay passing all messages to snapshot store by <paramref name="delay"/>.
        /// </summary>
        /// <remarks>
        ///     Each message will be delayed individually.
        /// </remarks>
        /// <param name="delay">Time by which recovery operation will be delayed.</param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        public Task PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Noop.Instance));
        }

        /// <summary>
        ///     Always fail all messages.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///         and similar issues with underlying snapshot store.
        ///     </para>
        /// </remarks>
        public Task Fail() => SetInterceptorAsync(SnapshotStoreInterceptors.Failure.Instance);

        /// <summary>
        ///     Fail message if predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///         and similar issues with underlying snapshot store.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailIf(InterceptorPredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance));
        }

        /// <summary>
        ///     Fail message if async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///         and similar issues with underlying snapshot store.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailIf(AsyncInterceptorPredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance));
        }

        /// <summary>
        ///     Fail message unless predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///         and similar issues with underlying snapshot store.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailUnless(InterceptorPredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance, negate: true));
        }

        /// <summary>
        ///     Fail message unless async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///         and similar issues with underlying snapshot store.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailUnless(AsyncInterceptorPredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(predicate, SnapshotStoreInterceptors.Failure.Instance, negate: true));
        }

        /// <summary>
        ///     Fail message after specified delay.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        public Task FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance));
        }

        /// <summary>
        ///     Fail message after specified delay if async predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailIfWithDelay(TimeSpan delay, AsyncInterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance)
            ));
        }

        /// <summary>
        ///     Fail message after specified delay if predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailIfWithDelay(TimeSpan delay, InterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance)
            ));
        }

        /// <summary>
        ///     Fail message after specified delay unless predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailUnlessWithDelay(TimeSpan delay, InterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance),
                negate: true
            ));
        }

        /// <summary>
        ///     Fail message after specified delay unless async predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Snapshot store will crash and actor will receive one of <see cref="SaveSnapshotFailure"/>,
        ///         <see cref="DeleteSnapshotFailure"/> or <see cref="DeleteSnapshotsFailure"/> messages.
        ///     </para>
        ///     <para>
        ///         Use this snapshot store behavior when it is needed to verify how well a persistent actor will handle network problems
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task FailUnlessWithDelay(TimeSpan delay, AsyncInterceptorPredicate predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            
            return SetInterceptorAsync(new SnapshotStoreInterceptors.OnCondition(
                predicate, 
                new SnapshotStoreInterceptors.Delay(delay, SnapshotStoreInterceptors.Failure.Instance),
                negate: true
            ));
        }
    }
}
