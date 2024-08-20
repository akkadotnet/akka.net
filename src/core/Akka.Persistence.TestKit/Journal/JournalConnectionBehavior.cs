//-----------------------------------------------------------------------
// <copyright file="JournalRecoveryBehavior.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

/// <summary>
///     Built-in Journal interceptors who will alter messages Recovery and/or Write of <see cref="TestJournal"/>.
/// </summary>
public class JournalConnectionBehavior
{
    internal JournalConnectionBehavior(IJournalConnectionBehaviorSetter setter)
    {
        Setter = setter;
    }

    private IJournalConnectionBehaviorSetter Setter { get; }

    /// <summary>
    ///     Use custom, user defined interceptor.
    /// </summary>
    /// <param name="interceptor">User defined interceptor which implements <see cref="IJournalInterceptor"/> interface.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="interceptor"/> is <c>null</c>.</exception>
    public Task SetInterceptorAsync(IConnectionInterceptor interceptor)
    {
        if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));

        return Setter.SetInterceptorAsync(interceptor);
    }

    /// <summary>
    ///     Pass all messages to journal without interfering.
    /// </summary>
    /// <remarks>
    ///     By using this interceptor <see cref="TestJournal"/> all journal operations will work like
    ///     in standard <see cref="Akka.Persistence.Journal.MemoryJournal"/>.
    /// </remarks>
    public Task Pass() => SetInterceptorAsync(ConnectionInterceptors.Noop.Instance);

    /// <summary>
    ///     Delay passing all messages to journal by <paramref name="delay"/>.
    /// </summary>
    /// <remarks>
    ///     Each message will be delayed individually.
    /// </remarks>
    /// <param name="delay">Time by which recovery operation will be delayed.</param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public Task PassWithDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));

        return SetInterceptorAsync(new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Noop.Instance));
    }

    /// <summary>
    ///     Always fail all messages.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    public Task Fail() => SetInterceptorAsync(ConnectionInterceptors.Failure.Instance);

    /// <summary>
    ///     Fail message if predicate <paramref name="predicate"/> will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailIf(Func<bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(predicate, ConnectionInterceptors.Failure.Instance));
    }

    /// <summary>
    ///     Fail message if async predicate <paramref name="predicate"/> will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailIf(Func<Task<bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(predicate, ConnectionInterceptors.Failure.Instance));
    }

    /// <summary>
    ///     Fail message unless predicate <paramref name="predicate"/> will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailUnless(Func<bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(predicate, ConnectionInterceptors.Failure.Instance, negate: true));
    }

    /// <summary>
    ///     Fail message unless async predicate <paramref name="predicate"/> will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailUnless(Func<Task<bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(predicate, ConnectionInterceptors.Failure.Instance, negate: true));
    }

    /// <summary>
    ///     Fail message after specified delay.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each message will be delayed individually.
    ///     </para>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="delay"></param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public Task FailWithDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)  throw new ArgumentException("Delay must be greater than zero", nameof(delay));

        return SetInterceptorAsync(new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Failure.Instance));
    }

    /// <summary>
    ///     Fail message after specified delay if async predicate <paramref name="predicate"/>
    ///     will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each message will be delayed individually.
    ///     </para>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="delay"></param>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailIfWithDelay(TimeSpan delay, Func<Task<bool>> predicate)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(
            predicate, 
            new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Failure.Instance)
        ));
    }

    /// <summary>
    ///     Fail message after specified delay if predicate <paramref name="predicate"/>
    ///     will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each message will be delayed individually.
    ///     </para>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="delay"></param>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailIfWithDelay(TimeSpan delay, Func<bool> predicate)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(
            predicate, 
            new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Failure.Instance)
        ));
    }

    /// <summary>
    ///     Fail message after specified delay unless predicate <paramref name="predicate"/>
    ///     will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each message will be delayed individually.
    ///     </para>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="delay"></param>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailUnlessWithDelay(TimeSpan delay, Func<bool> predicate)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(
            predicate, 
            new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Failure.Instance),
            negate: true
        ));
    }

    /// <summary>
    ///     Fail message after specified delay unless async predicate <paramref name="predicate"/>
    ///     will return <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each message will be delayed individually.
    ///     </para>
    ///     <para>
    ///         Journal will crash and <see cref="Eventsourced.OnPersistFailure">UntypedPersistentActor.OnPersistFailure</see> will be called on persistent actor.
    ///     </para>
    ///     <para>
    ///         Use this Journal behavior when it is needed to verify how well a persistent actor will handle network problems
    ///         and similar issues with underlying journal.
    ///     </para>
    /// </remarks>
    /// <param name="delay"></param>
    /// <param name="predicate"></param>
    /// <exception cref="ArgumentException">When <paramref name="delay"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="predicate"/> is <c>null</c>.</exception>
    public Task FailUnlessWithDelay(TimeSpan delay, Func<Task<bool>> predicate)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentException("Delay must be greater than zero", nameof(delay));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            
        return SetInterceptorAsync(new ConnectionInterceptors.OnCondition(
            predicate, 
            new ConnectionInterceptors.Delay(delay, ConnectionInterceptors.Failure.Instance),
            negate: true
        ));
    }
}