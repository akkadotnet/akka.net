// -----------------------------------------------------------------------
//  <copyright file="TypeHints.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#if AOT_ENABLED
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Actor.Internal;

/// <summary>
/// Contains type hints for AOT compilation to avoid dynamic type loading.
/// All types referenced here will be preserved by the AOT trimmer.
/// </summary>
internal sealed class TypeHints
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static readonly Type DefaultActorRefProviderType = typeof(LocalActorRefProvider);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static readonly Type DefaultSchedulerType = typeof(HashedWheelTimerScheduler);

    /// <summary>
    /// Default mailbox requirement mappings from akka.conf.
    /// Maps message queue semantic types to their corresponding mailbox configuration paths.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> DefaultMailboxRequirements = new Dictionary<Type, string>
    {
        { typeof(IUnboundedMessageQueueSemantics), "akka.actor.mailbox.unbounded-queue-based" },
        { typeof(IBoundedMessageQueueSemantics), "akka.actor.mailbox.bounded-queue-based" },
        { typeof(IDequeBasedMessageQueueSemantics), "akka.actor.mailbox.unbounded-deque-based" },
        { typeof(IUnboundedDequeBasedMessageQueueSemantics), "akka.actor.mailbox.unbounded-deque-based" },
        { typeof(IBoundedDequeBasedMessageQueueSemantics), "akka.actor.mailbox.bounded-deque-based" },
        { typeof(IMultipleConsumerSemantics), "akka.actor.mailbox.unbounded-queue-based" },
        { typeof(ILoggerMessageQueueSemantics), "akka.actor.mailbox.logger-queue" }
    };
}
#endif