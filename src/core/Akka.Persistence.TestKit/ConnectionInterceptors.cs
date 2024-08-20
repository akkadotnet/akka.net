// -----------------------------------------------------------------------
// <copyright file="ConnectionInterceptors.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

public static class ConnectionInterceptors
{
    public sealed class Noop : IConnectionInterceptor
    {
        public static readonly IConnectionInterceptor Instance = new Noop();

        public Task InterceptAsync() => Task.FromResult(true);
    }

    public sealed class Failure : IConnectionInterceptor
    {
        public static readonly IConnectionInterceptor Instance = new Failure();

        public Task InterceptAsync() => throw new TestConnectionException(); 
    }

    public sealed class Delay : IConnectionInterceptor
    {
        public Delay(TimeSpan delay, IConnectionInterceptor next)
        {
            _delay = delay;
            _next = next;
        }

        private readonly TimeSpan _delay;
        private readonly IConnectionInterceptor _next;

        public async Task InterceptAsync()
        {
            await Task.Delay(_delay);
            await _next.InterceptAsync();
        }
    }

    public sealed class OnCondition : IConnectionInterceptor
    {
        public OnCondition(Func<Task<bool>> predicate, IConnectionInterceptor next, bool negate = false)
        {
            _predicate = predicate;
            _next = next;
            _negate = negate;
        }

        public OnCondition(Func<bool> predicate, IConnectionInterceptor next, bool negate = false)
        {
            _predicate = () => Task.FromResult(predicate());
            _next = next;
            _negate = negate;
        }

        private readonly Func<Task<bool>> _predicate;
        private readonly IConnectionInterceptor _next;
        private readonly bool _negate;

        public async Task InterceptAsync()
        {
            var result = await _predicate();
            if ((_negate && !result) || (!_negate && result))
            {
                await _next.InterceptAsync();
            }
        }
    }
    
    public sealed class CancelableDelay: IConnectionInterceptor
    {
        public CancelableDelay(TimeSpan delay, IConnectionInterceptor next, CancellationToken cancellationToken)
        {
            _delay = delay;
            _next = next;
            _cancellationToken = cancellationToken;
        }

        private readonly TimeSpan _delay;
        private readonly IConnectionInterceptor _next;
        private readonly CancellationToken _cancellationToken;

        public async Task InterceptAsync()
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
            await _next.InterceptAsync();
        }
    }
}