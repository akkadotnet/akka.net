//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreInterceptors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    internal static class SnapshotStoreInterceptors
    {
        internal class Noop : ISnapshotStoreInterceptor
        {
            public static readonly ISnapshotStoreInterceptor Instance = new Noop();

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria) => Task.FromResult(true);
        }

        internal class Failure : ISnapshotStoreInterceptor
        {
            public static readonly ISnapshotStoreInterceptor Instance = new Failure();

            public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria) => throw new TestSnapshotStoreFailureException(); 
        }

        internal class Delay : ISnapshotStoreInterceptor
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

        internal sealed class OnCondition : ISnapshotStoreInterceptor
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
    }
}
