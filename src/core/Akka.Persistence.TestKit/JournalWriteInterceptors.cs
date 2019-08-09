namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    internal static class JournalWriteInterceptors
    {
        internal class Noop : IJournalWriteInterceptor
        {
            public static readonly IJournalWriteInterceptor Instance = new Noop();

            public Task InterceptAsync(IPersistentRepresentation message) => Task.FromResult(true);
        }

        internal class Failure : IJournalWriteInterceptor
        {
            public static readonly IJournalWriteInterceptor Instance = new Failure();

            public Task InterceptAsync(IPersistentRepresentation message) => throw new TestJournalFailureException(); 
        }

        internal class Rejection : IJournalWriteInterceptor
        {
            public static readonly IJournalWriteInterceptor Instance = new Rejection();

            public Task InterceptAsync(IPersistentRepresentation message) => throw new TestJournalRejectionException(); 
        }
        
        internal class Delay : IJournalWriteInterceptor
        {
            public Delay(TimeSpan delay, IJournalWriteInterceptor next)
            {
                _delay = delay;
                _next = next;
            }

            private readonly TimeSpan _delay;
            private readonly IJournalWriteInterceptor _next;

            public async Task InterceptAsync(IPersistentRepresentation message)
            {
                await Task.Delay(_delay);
                await _next.InterceptAsync(message);
            }
        }

        internal sealed class OnCondition : IJournalWriteInterceptor
        {
            public OnCondition(Func<IPersistentRepresentation, Task<bool>> predicate, IJournalWriteInterceptor next, bool negate = false)
            {
                _predicate = predicate;
                _next = next;
                _negate = negate;
            }

            public OnCondition(Func<IPersistentRepresentation, bool> predicate, IJournalWriteInterceptor next, bool negate = false)
            {
                _predicate = message => Task.FromResult(predicate(message));
                _next = next;
                _negate = negate;
            }

            private readonly Func<IPersistentRepresentation, Task<bool>> _predicate;
            private readonly IJournalWriteInterceptor _next;
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

        internal class OnType : IJournalWriteInterceptor
        {
            public OnType(Type messageType, IJournalWriteInterceptor next)
            {
                _messageType = messageType;
                _next = next;
            }

            private readonly Type _messageType;
            private readonly IJournalWriteInterceptor _next;

            public async Task InterceptAsync(IPersistentRepresentation message)
            {
                var type = message.Payload.GetType();

                if (_messageType.IsAssignableFrom(type))
                {
                    await _next.InterceptAsync(message);
                }
            }
        }
    }
}