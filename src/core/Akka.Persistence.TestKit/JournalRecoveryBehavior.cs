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

        protected void SetInterceptor(IJournalInterceptor interceptor) => Setter.SetInterceptor(interceptor);

        public void Pass() => SetInterceptor(JournalInterceptors.Noop.Instance);

        public void PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            SetInterceptor(new JournalInterceptors.Delay(delay, JournalInterceptors.Noop.Instance));
        }

        public void Fail() => SetInterceptor(JournalInterceptors.Failure.Instance);

        public void FailOnType<TMessage>() => FailOnType(typeof(TMessage));

        public void FailOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            SetInterceptor(new JournalInterceptors.OnType(messageType, JournalInterceptors.Failure.Instance));
        }

        public void FailIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        public void FailIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        public void FailUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        public void FailUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        public void FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            SetInterceptor(new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance));
        }

        public void FailIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }

        public void FailIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }

        public void FailUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance),
                negate: true
            ));
        }

        public void FailUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            
            SetInterceptor(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance),
                negate: true
            ));
        }

        public void FailOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        public void FailOnTypeWithDelay(TimeSpan delay, Type messageType)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            SetInterceptor(new JournalInterceptors.OnType(
                messageType, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance)
            ));
        }
    }
}
