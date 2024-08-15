// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreInterceptors.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

internal static class SnapshotStoreInterceptors
{
    internal class Noop : ISnapshotStoreInterceptor
    {
        public static readonly ISnapshotStoreInterceptor Instance = new Noop();

        public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            return Task.FromResult(true);
        }
    }

    internal class Failure : ISnapshotStoreInterceptor
    {
        public static readonly ISnapshotStoreInterceptor Instance = new Failure();

        public Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            throw new TestSnapshotStoreFailureException();
        }
    }

    internal class Delay : ISnapshotStoreInterceptor
    {
        private readonly TimeSpan _delay;
        private readonly ISnapshotStoreInterceptor _next;

        public Delay(TimeSpan delay, ISnapshotStoreInterceptor next)
        {
            _delay = delay;
            _next = next;
        }

        public async Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            await Task.Delay(_delay);
            await _next.InterceptAsync(persistenceId, criteria);
        }
    }

    internal sealed class OnCondition : ISnapshotStoreInterceptor
    {
        private readonly bool _negate;
        private readonly ISnapshotStoreInterceptor _next;

        private readonly Func<string, SnapshotSelectionCriteria, Task<bool>> _predicate;

        public OnCondition(Func<string, SnapshotSelectionCriteria, Task<bool>> predicate,
            ISnapshotStoreInterceptor next, bool negate = false)
        {
            _predicate = predicate;
            _next = next;
            _negate = negate;
        }

        public OnCondition(Func<string, SnapshotSelectionCriteria, bool> predicate, ISnapshotStoreInterceptor next,
            bool negate = false)
        {
            _predicate = (persistenceId, criteria) => Task.FromResult(predicate(persistenceId, criteria));
            _next = next;
            _negate = negate;
        }

        public async Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var result = await _predicate(persistenceId, criteria);
            if ((_negate && !result) || (!_negate && result)) await _next.InterceptAsync(persistenceId, criteria);
        }
    }
}