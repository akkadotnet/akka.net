// -----------------------------------------------------------------------
//  <copyright file="JournalInterceptors.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

internal static class JournalInterceptors
{
    internal class Noop : IJournalInterceptor
    {
        public static readonly IJournalInterceptor Instance = new Noop();

        public Task InterceptAsync(IPersistentRepresentation message)
        {
            return Task.FromResult(true);
        }
    }

    internal class Failure : IJournalInterceptor
    {
        public static readonly IJournalInterceptor Instance = new Failure();

        public Task InterceptAsync(IPersistentRepresentation message)
        {
            throw new TestJournalFailureException();
        }
    }

    internal class Rejection : IJournalInterceptor
    {
        public static readonly IJournalInterceptor Instance = new Rejection();

        public Task InterceptAsync(IPersistentRepresentation message)
        {
            throw new TestJournalRejectionException();
        }
    }

    internal class Delay : IJournalInterceptor
    {
        private readonly TimeSpan _delay;
        private readonly IJournalInterceptor _next;

        public Delay(TimeSpan delay, IJournalInterceptor next)
        {
            _delay = delay;
            _next = next;
        }

        public async Task InterceptAsync(IPersistentRepresentation message)
        {
            await Task.Delay(_delay);
            await _next.InterceptAsync(message);
        }
    }

    internal sealed class OnCondition : IJournalInterceptor
    {
        private readonly bool _negate;
        private readonly IJournalInterceptor _next;

        private readonly Func<IPersistentRepresentation, Task<bool>> _predicate;

        public OnCondition(Func<IPersistentRepresentation, Task<bool>> predicate, IJournalInterceptor next,
            bool negate = false)
        {
            _predicate = predicate;
            _next = next;
            _negate = negate;
        }

        public OnCondition(Func<IPersistentRepresentation, bool> predicate, IJournalInterceptor next,
            bool negate = false)
        {
            _predicate = message => Task.FromResult(predicate(message));
            _next = next;
            _negate = negate;
        }

        public async Task InterceptAsync(IPersistentRepresentation message)
        {
            var result = await _predicate(message);
            if ((_negate && !result) || (!_negate && result)) await _next.InterceptAsync(message);
        }
    }

    internal class OnType : IJournalInterceptor
    {
        private readonly Type _messageType;
        private readonly IJournalInterceptor _next;

        public OnType(Type messageType, IJournalInterceptor next)
        {
            _messageType = messageType;
            _next = next;
        }

        public async Task InterceptAsync(IPersistentRepresentation message)
        {
            var type = message.Payload.GetType();

            if (_messageType.IsAssignableFrom(type)) await _next.InterceptAsync(message);
        }
    }
}