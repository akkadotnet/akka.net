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

    /// <summary>
    /// Collection of standard Journal interceptors who will alter Recovery operation og <see cref="TestJournal"/>.
    /// </summary>
    public sealed class JournalRecoveryBehavior
    {
        internal JournalRecoveryBehavior(IJournalBehaviorSetter setter)
        {
            this.Setter = setter;
        }

        private IJournalBehaviorSetter Setter { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="interceptor"></param>
        /// <returns></returns>
        public Task SetInterceptorAsync(IJournalInterceptor interceptor) => Setter.SetInterceptorAsync(interceptor);

        /// <summary>
        /// Use interceptor who will do nothing.
        /// </summary>
        /// <remarks>
        /// By using this interceptor <see cref="TestJournal"/> recovery operation will work like
        /// standard <see cref="Akka.Persistence.Journal.MemoryJournal"/>.
        /// </remarks>
        /// <returns></returns>
        public Task Pass() => SetInterceptorAsync(JournalInterceptors.Noop.Instance);

        /// <summary>
        /// Delay recovery by <paramref name="delay"/>.
        /// </summary>
        /// <param name="delay">Time by which recovery operation will be delayed.</param>
        /// <returns></returns>
        public Task PassWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Noop.Instance));
        }

        /// <summary>
        /// Always fail on recovery.
        /// </summary>
        /// <returns></returns>
        public Task Fail() => SetInterceptorAsync(JournalInterceptors.Failure.Instance);

        /// <summary>
        /// Fail on recovery during recovering message of type <typeparamref name="TMessage"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///     If <typeparamref name="TMessage"/> is interface, recovery will fail when message implements that interface.
        /// </para>
        /// <para>
        ///     If <typeparamref name="TMessage"/> is class, recovery will fail when message can be assigned to <typeparamref name="TMessage"/>.
        /// </para>
        /// </remarks>
        /// <typeparam name="TMessage"></typeparam>
        /// <returns></returns>
        public Task FailOnType<TMessage>() => FailOnType(typeof(TMessage));

        /// <summary>
        /// Fail on recovery during recovering message of type <paramref name="messageType"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///     If <paramref name="messageType"/> is interface, recovery will fail when message implements that interface.
        /// </para>
        /// <para>
        ///     If <paramref name="messageType"/> is class, recovery will fail when message can be assigned to <paramref name="messageType"/>.
        /// </para>
        /// </remarks>
        /// <param name="messageType"></param>
        /// <returns></returns>
        public Task FailOnType(Type messageType)
        {
            if (messageType is null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnType(messageType, JournalInterceptors.Failure.Instance));
        }

        /// <summary>
        /// Fail on recovery if predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public Task FailIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        /// <summary>
        /// Fail on recovery if async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public Task FailIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance));
        }

        /// <summary>
        /// Fail on recovery unless predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public Task FailUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        /// <summary>
        /// Fail on recovery unless async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public Task FailUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Failure.Instance, negate: true));
        }

        /// <summary>
        /// Fail on recovery after specified delay.
        /// </summary>
        /// <param name="delay"></param>
        /// <returns></returns>
        public Task FailWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            }

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Failure.Instance));
        }

        /// <summary>
        /// Fail on recovery with specified delay if async predicate <paramref name="predicate"/>
        /// will return <c>true</c>.
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Fail on recovery with specified delay if predicate <paramref name="predicate"/>
        /// will return <c>true</c>.
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Fail on recovery with specified delay unless predicate <paramref name="predicate"/>
        /// will return <c>true</c>.
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Fail on recovery with specified delay unless async predicate <paramref name="predicate"/>
        /// will return <c>true</c>.
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Fail on recovery with specified delay if recovering message of type <typeparamref name="TMessage"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///     If <paramref name="messageType"/> is interface, recovery will fail when message implements that interface.
        /// </para>
        /// <para>
        ///     If <paramref name="messageType"/> is class, recovery will fail when message can be assigned to <paramref name="messageType"/>.
        /// </para>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="delay"></param>
        /// <returns></returns>
        public Task FailOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        /// <summary>
        /// Fail on recovery with specified delay if recovering message of type <paramref name="messageType"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///     If <paramref name="messageType"/> is interface, recovery will fail when message implements that interface.
        /// </para>
        /// <para>
        ///     If <paramref name="messageType"/> is class, recovery will fail when message can be assigned to <paramref name="messageType"/>.
        /// </para>
        /// <param name="delay"></param>
        /// <param name="messageType"></param>
        /// <returns></returns>
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
