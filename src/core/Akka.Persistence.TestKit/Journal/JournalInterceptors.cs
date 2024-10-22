//-----------------------------------------------------------------------
// <copyright file="JournalInterceptors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    public static class JournalInterceptors
    {
        public sealed class Noop : IJournalInterceptor
        {
            public static readonly IJournalInterceptor Instance = new Noop();

            public Task InterceptAsync(IPersistentRepresentation message) => Task.FromResult(true);
        }

        public sealed class Failure : IJournalInterceptor
        {
            public static readonly IJournalInterceptor Instance = new Failure();

            public Task InterceptAsync(IPersistentRepresentation message) => throw new TestJournalFailureException(); 
        }

        public sealed class Rejection : IJournalInterceptor
        {
            public static readonly IJournalInterceptor Instance = new Rejection();

            public Task InterceptAsync(IPersistentRepresentation message) => throw new TestJournalRejectionException(); 
        }
        
        public sealed class Delay : IJournalInterceptor
        {
            public Delay(TimeSpan delay, IJournalInterceptor next)
            {
                _delay = delay;
                _next = next;
            }

            private readonly TimeSpan _delay;
            private readonly IJournalInterceptor _next;

            public async Task InterceptAsync(IPersistentRepresentation message)
            {
                await Task.Delay(_delay);
                await _next.InterceptAsync(message);
            }
        }

        public sealed class OnCondition : IJournalInterceptor
        {
            public OnCondition(Func<IPersistentRepresentation, Task<bool>> predicate, IJournalInterceptor next, bool negate = false)
            {
                _predicate = predicate;
                _next = next;
                _negate = negate;
            }

            public OnCondition(Func<IPersistentRepresentation, bool> predicate, IJournalInterceptor next, bool negate = false)
            {
                _predicate = message => Task.FromResult(predicate(message));
                _next = next;
                _negate = negate;
            }

            private readonly Func<IPersistentRepresentation, Task<bool>> _predicate;
            private readonly IJournalInterceptor _next;
            private readonly bool _negate;

            public async Task InterceptAsync(IPersistentRepresentation message)
            {
                var result = await _predicate(message);
                if ((_negate && !result) || (!_negate && result))
                {
                    await _next.InterceptAsync(message);
                }
            }
        }

        public sealed class OnType : IJournalInterceptor
        {
            public OnType(Type messageType, IJournalInterceptor next)
            {
                _messageType = messageType;
                _next = next;
            }

            private readonly Type _messageType;
            private readonly IJournalInterceptor _next;

            public async Task InterceptAsync(IPersistentRepresentation message)
            {
                var type = message.Payload.GetType();

                if (_messageType.IsAssignableFrom(type))
                {
                    await _next.InterceptAsync(message);
                }
            }
        }
        
        public sealed class CancelableDelay: IJournalInterceptor
        {
            public CancelableDelay(TimeSpan delay, IJournalInterceptor next, CancellationToken cancellationToken)
            {
                _delay = delay;
                _next = next;
                _cancellationToken = cancellationToken;
            }

            private readonly TimeSpan _delay;
            private readonly IJournalInterceptor _next;
            private readonly CancellationToken _cancellationToken;

            public async Task InterceptAsync(IPersistentRepresentation message)
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
                await _next.InterceptAsync(message);
            }
        }
    }
}
