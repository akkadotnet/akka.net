//-----------------------------------------------------------------------
// <copyright file="JournalWriteBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    ///     Built-in Journal interceptors who will alter message Write of <see cref="TestJournal"/>.
    /// </summary>
    public class JournalWriteBehavior : JournalRecoveryBehavior
    {
        internal JournalWriteBehavior(IJournalBehaviorSetter setter) : base(setter) { }

        /// <summary>
        ///     Always reject all messages.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        public Task Reject() => SetInterceptorAsync(JournalInterceptors.Rejection.Instance);

        /// <summary>
        ///     Reject message during processing message of type <typeparamref name="TMessage"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        ///     <para>
        ///         <list type="bullet">
        ///             <item>If <typeparamref name="TMessage"/> is interface, journal will reject when message implements that interface.</item>
        ///             <item>If <typeparamref name="TMessage"/> is class, journal will reject when message can be assigned to <typeparamref name="TMessage"/>.</item>
        ///         </list>
        ///     </para>
        /// </remarks>
        /// <typeparam name="TMessage"></typeparam>
        public Task RejectOnType<TMessage>() => FailOnType(typeof(TMessage));

        /// <summary>
        ///     Reject message during processing message of type <paramref name="messageType"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see>> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        ///     <para>
        ///         <list type="bullet">
        ///             <item>If <paramref name="messageType"/> is interface, journal will reject when message implements that interface.</item>
        ///             <item>If <paramref name="messageType"/> is class, journal will reject when message can be assigned to <paramref name="messageType"/>.</item>
        ///         </list>
        ///     </para>
        /// </remarks>
        /// <param name="messageType"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="messageType"/> is <c>null</c>.</exception>
        public Task RejectOnType(Type messageType)
        {
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            return SetInterceptorAsync(new JournalInterceptors.OnType(messageType, JournalInterceptors.Rejection.Instance));
        }

        /// <summary>
        ///     Reject message if predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectIf(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance));
        }

        /// <summary>
        ///     Reject message if async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectIf(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance));
        }

        /// <summary>
        ///     Reject message unless predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectUnless(Func<IPersistentRepresentation, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance, negate: true));
        }

        /// <summary>
        ///     Reject message unless async predicate <paramref name="predicate"/> will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectUnless(Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(predicate, JournalInterceptors.Rejection.Instance, negate: true));
        }

        /// <summary>
        ///     Reject message after specified delay.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        public Task RejectWithDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));

            return SetInterceptorAsync(new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance));
        }

        /// <summary>
        ///     Reject message after specified delay if async predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }

        /// <summary>
        ///     Reject message after specified delay if predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectIfWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }

        /// <summary>
        ///     Reject message after specified delay unless predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, bool> predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        /// <summary>
        ///     Reject message after specified delay unless async predicate <paramref name="predicate"/>
        ///     will return <c>true</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        /// </remarks>
        /// <param name="delay"></param>
        /// <param name="predicate"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
        public Task RejectUnlessWithDelay(TimeSpan delay, Func<IPersistentRepresentation, Task<bool>> predicate)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            
            return SetInterceptorAsync(new JournalInterceptors.OnCondition(
                predicate, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance),
                negate: true
            ));
        }

        /// <summary>
        ///     Reject message after specified delay if recovering message of type <typeparamref name="TMessage"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        ///     <para>
        ///         <list type="bullet">
        ///             <item>If <typeparamref name="TMessage"/> is interface, journal will reject when message implements that interface.</item>
        ///             <item>If <typeparamref name="TMessage"/> is class, journal will reject when message can be assigned to <typeparamref name="TMessage"/>.</item>
        ///         </list>
        ///     </para>
        /// </remarks>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="delay"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        public Task RejectOnTypeWithDelay<TMessage>(TimeSpan delay) => FailOnTypeWithDelay(delay, typeof(TMessage));

        /// <summary>
        ///     Reject message after specified delay if recovering message of type <paramref name="messageType"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each message will be delayed individually.
        ///     </para>
        ///     <para>
        ///         Journal will **NOT** crash, but <see cref="Eventsourced.OnPersistRejected">UntypedPersistentActor.OnPersistRejected</see> will be called
        ///         on each rejected message.
        ///     </para>
        ///     <para>
        ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle serialization,
        ///         type conversion and other local problems withing journal.
        ///     </para>
        ///     <para>
        ///         <list type="bullet">
        ///             <item>If <paramref name="messageType"/> is interface, journal will reject when message implements that interface.</item>
        ///             <item>If <paramref name="messageType"/> is class, journal will reject when message can be assigned to <paramref name="messageType"/>.</item>
        ///         </list>
        ///     </para>
        /// </remarks>
        /// <param name="messageType"></param>
        /// <param name="delay"></param>
        /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <exception cref="ArgumentNullException">When <paramref name="messageType"/> is <c>null</c>.</exception>
        public Task RejectOnTypeWithDelay(TimeSpan delay, Type messageType)
        {
            if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            return SetInterceptorAsync(new JournalInterceptors.OnType(
                messageType, 
                new JournalInterceptors.Delay(delay, JournalInterceptors.Rejection.Instance)
            ));
        }
    }
}
