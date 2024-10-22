//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreInterceptors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    public static class SnapshotStoreInterceptors
    {
        public sealed class Noop : ISnapshotStoreInterceptor
        {
            public static readonly ISnapshotStoreInterceptor Instance = new Noop();

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria) => Task.FromResult(true);
        }

        public sealed  class Failure : ISnapshotStoreInterceptor
        {
            public static readonly ISnapshotStoreInterceptor Instance = new Failure();

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria) => throw new TestSnapshotStoreFailureException(); 
        }

        public sealed  class Delay : ISnapshotStoreInterceptor
        {
            public Delay(TimeSpan delay, ISnapshotStoreInterceptor next)
            {
                _delay = delay;
                _next = next;
            }

            private readonly TimeSpan _delay;
            private readonly ISnapshotStoreInterceptor _next;

            public async Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                await Task.Delay(_delay);
                await _next.InterceptAsync(persistenceId, criteria);
            }
        }

        public sealed class OnCondition : ISnapshotStoreInterceptor
        {
            public OnCondition(Func<string, SnapshotSelectionCriteria, Task<bool>> predicate, ISnapshotStoreInterceptor next, bool negate = false)
            {
                _predicate = predicate;
                _next = next;
                _negate = negate;
            }

            public OnCondition(Func<string, SnapshotSelectionCriteria, bool> predicate, ISnapshotStoreInterceptor next, bool negate = false)
            {
                _predicate = (persistenceId, criteria) => Task.FromResult(predicate(persistenceId, criteria));
                _next = next;
                _negate = negate;
            }

            private readonly Func<string, SnapshotSelectionCriteria, Task<bool>> _predicate;
            private readonly ISnapshotStoreInterceptor _next;
            private readonly bool _negate;

            public async Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                var result = await _predicate(persistenceId, criteria);
                if ((_negate && !result) || (!_negate && result))
                {
                    await _next.InterceptAsync(persistenceId, criteria);
                }
            }
        }
        
        public sealed class CancelableDelay: ISnapshotStoreInterceptor
        {
            public CancelableDelay(TimeSpan delay, ISnapshotStoreInterceptor next, CancellationToken cancellationToken)
            {
                _delay = delay;
                _next = next;
                _cancellationToken = cancellationToken;
            }

            private readonly TimeSpan _delay;
            private readonly ISnapshotStoreInterceptor _next;
            private readonly CancellationToken _cancellationToken;

            public async Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                try
                {
                    await Task.Delay(_delay, _cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // no-op
                }
                catch (TimeoutException)
                {
                    // no-op
                }
                await _next.InterceptAsync(persistenceId, criteria);
            }
        }
    }
}
