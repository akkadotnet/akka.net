//-----------------------------------------------------------------------
// <copyright file="JournalWriteBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    public class JournalWriteBehavior : JournalRecoveryBehavior
    {
        internal JournalWriteBehavior(IJournalBehaviorSetter setter) : base(setter) { }

        public Task Reject() => SetInterceptorAsync(JournalInterceptors.Rejection.Instance);

        public Task RejectOnType<TMessage>() => FailOnType(typeof(TMessage));

        public Task RejectOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnType(messageType, JournalInterceptors.Rejection.Instance));
        }

        public Task RejectIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance));
        }

        public Task RejectIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance));
        }

        public Task RejectUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance, negate: true));
        }

        public Task RejectUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance, negate: true));
        }

        public Task RejectWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance));
        }

        public Task RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
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
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }

        public Task RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
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
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }

        public Task RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
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
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        public Task RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
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
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        public Task RejectOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        public Task RejectOnTypeWithDelay(TimeSpan delay, Type messageType)
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
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }
    }
}