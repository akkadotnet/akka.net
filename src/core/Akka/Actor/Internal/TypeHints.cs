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
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Routing;
using Akka.Serialization;

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
    /// Default logger types from akka.conf.
    /// Maps logger type names to their implementation types.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Type> DefaultLoggers = new Dictionary<string, Type>
    {
        { "Akka.Event.DefaultLogger", typeof(Event.DefaultLogger) },
        { "Akka.Event.TraceLogger", typeof(Event.TraceLogger) }
    };

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

    /// <summary>
    /// Default serializer factory functions from akka.conf.
    /// Maps serializer alias names to factory functions that create serializer instances.
    /// Using factory functions avoids reflection and is fully AOT-compatible.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<ExtendedActorSystem, Config, Serializer>> DefaultSerializerFactories = new Dictionary<string, Func<ExtendedActorSystem, Config, Serializer>>
    {
        { "bytes", (system, config) => new ByteArraySerializer(system) }
        // JSON serializer commented out - not AOT compatible
        // { "json", (system, config) => config.IsNullOrEmpty()
        //     ? new NewtonSoftJsonSerializer(system)
        //     : new NewtonSoftJsonSerializer(system, config) }
    };

    /// <summary>
    /// Default serialization bindings from akka.conf.
    /// Maps message types to their serializer alias names.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, string> DefaultSerializerBindings = new Dictionary<Type, string>
    {
        { typeof(byte[]), "bytes" },
        //{ typeof(object), "json" }
    };

    /// <summary>
    /// Default router factory functions from akka.conf.
    /// Maps router type aliases to factory functions that create router config instances.
    /// Using factory functions avoids reflection and is fully AOT-compatible.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<Config, RouterConfig>> DefaultRouterFactories = new Dictionary<string, Func<Config, RouterConfig>>
    {
        { "from-code", (config) => NoRouter.Instance },
        { "round-robin-pool", (config) => new RoundRobinPool(config) },
        { "round-robin-group", (config) => new RoundRobinGroup(config) },
        { "random-pool", (config) => new RandomPool(config) },
        { "random-group", (config) => new RandomGroup(config) },
        { "smallest-mailbox-pool", (config) => new SmallestMailboxPool(config) },
        { "broadcast-pool", (config) => new BroadcastPool(config) },
        { "broadcast-group", (config) => new BroadcastGroup(config) },
        { "scatter-gather-pool", (config) => new ScatterGatherFirstCompletedPool(config) },
        { "scatter-gather-group", (config) => new ScatterGatherFirstCompletedGroup(config) },
        { "consistent-hashing-pool", (config) => new ConsistentHashingPool(config) },
        { "consistent-hashing-group", (config) => new ConsistentHashingGroup(config) },
        { "tail-chopping-pool", (config) => new TailChoppingPool(config) },
        { "tail-chopping-group", (config) => new TailChoppingGroup(config) }
        // cluster-metrics routers excluded - they're in Akka.Cluster.Metrics assembly
    };
}
#endif