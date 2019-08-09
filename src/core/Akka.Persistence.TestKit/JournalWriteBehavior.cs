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
    using Actor;

    public sealed class JournalWriteBehavior
    {
        internal JournalWriteBehavior(IActorRef journal)
        {
            this.journal = journal;
        }

        private readonly IActorRef journal;

        private void SetInterceptor(IJournalWriteInterceptor interceptor)
            =>  journal.Ask(
                    new TestJournal.UseWriteInterceptor(interceptor),
                    TimeSpan.FromSeconds(3)
                ).Wait();

        public void Pass() => SetInterceptor(JournalWriteInterceptors.Noop.Instance);

        public void PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            SetInterceptor(new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Noop.Instance));
        }

        public void Fail() => SetInterceptor(JournalWriteInterceptors.Failure.Instance);

        public void FailOnType<TMessage>() => FailOnType(typeof(TMessage));

        public void FailOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            SetInterceptor(new JournalWriteInterceptors.OnType(messageType, JournalWriteInterceptors.Failure.Instance));
        }

        public void FailIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Failure.Instance));
        }

        public void FailIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Failure.Instance));
        }

        public void FailUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Failure.Instance, negate: true));
        }

        public void FailUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Failure.Instance, negate: true));
        }

        public void FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            SetInterceptor(new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance));
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

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance)
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

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance)
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

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance),
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
            
            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance),
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

            SetInterceptor(new JournalWriteInterceptors.OnType(
                messageType, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Failure.Instance)
            ));
        }

        public void Reject() => SetInterceptor(JournalWriteInterceptors.Rejection.Instance);

        public void RejectOnType<TMessage>() => FailOnType(typeof(TMessage));

        public void RejectOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            SetInterceptor(new JournalWriteInterceptors.OnType(messageType, JournalWriteInterceptors.Rejection.Instance));
        }

        public void RejectIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Rejection.Instance));
        }

        public void RejectIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Rejection.Instance));
        }

        public void RejectUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Rejection.Instance, negate: true));
        }

        public void RejectUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(predicate, JournalWriteInterceptors.Rejection.Instance, negate: true));
        }

        public void RejectWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            SetInterceptor(new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance));
        }

        public void RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance)
            ));
        }

        public void RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance)
            ));
        }

        public void RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        public void RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            
            SetInterceptor(new JournalWriteInterceptors.OnCondition(
                predicate, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        public void RejectOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        public void RejectOnTypeWithDelay(TimeSpan delay, Type messageType)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }
            
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            SetInterceptor(new JournalWriteInterceptors.OnType(
                messageType, 
                new JournalWriteInterceptors.Delay(delay, JournalWriteInterceptors.Rejection.Instance)
            ));
        }
    }
}
