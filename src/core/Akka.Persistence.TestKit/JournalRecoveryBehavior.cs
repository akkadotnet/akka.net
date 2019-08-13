//-----------------------------------------------------------------------
// <copyright file="JournalRecoveryBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    public class JournalRecoveryBehavior
    {
        internal JournalRecoveryBehavior(IJournalBehaviorSetter setter)
        {
            this.Setter = setter;
        }

        protected readonly IJournalBehaviorSetter Setter;

        public Task SetInterceptorAsync(IJournalInterceptor interceptor) => Setter.SetInterceptorAsync(interceptor);

        public Task Pass() => SetInterceptorAsync(JournalInterceptors.Noop.Instance);

        public Task PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Noop.Instance));
        }

        public Task Fail() => SetInterceptorAsync(JournalInterceptors.Failure.Instance);

        public Task FailOnType<TMessage>() => FailOnType(typeof(TMessage));

        public Task FailOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnType(messageType, JournalInterceptors.Failure.Instance));
        }

        public Task FailIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        public Task FailIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        public Task FailUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        public Task FailUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        public Task FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance));
        }

        public Task FailIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }

        public Task FailIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }

        public Task FailUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance),
                negate: true
            ));
        }

        public Task FailUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            
            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance),
                negate: true
            ));
        }

        public Task FailOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        public Task FailOnTypeWithDelay(TimeSpan delay, Type messageType)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnType(
                messageType, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }
    }
}
