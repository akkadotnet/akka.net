//-----------------------------------------------------------------------
// <copyright file="LoggerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;

namespace Akka.Event;

/// <summary>
/// Represents a single logger entry registered via <see cref="LoggerSetup"/>.
/// </summary>
/// <remarks>
/// Supports two registration modes:
/// <list type="bullet">
///   <item><description>Type-based: the logger type is preserved for AOT and instantiated via <see cref="Props"/>.</description></item>
///   <item><description>Factory-based: a <see cref="Func{ActorSystemImpl, Props}"/> is used for loggers that require
///   custom initialization (e.g. access to the <see cref="ActorSystemImpl"/>).</description></item>
/// </list>
/// </remarks>
public sealed class LoggerRegistration
{
    /// <summary>
    /// Creates a type-based logger registration.
    /// </summary>
    /// <param name="loggerType">
    /// The type of the logger actor. Must derive from <see cref="ActorBase"/> or
    /// <see cref="MinimalLogger"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="loggerType"/> is null.</exception>
    public LoggerRegistration(Type loggerType)
    {
        LoggerType = loggerType ?? throw new ArgumentNullException(nameof(loggerType));
        PropsFactory = null;
    }

    /// <summary>
    /// Creates a factory-based logger registration.
    /// </summary>
    /// <param name="propsFactory">
    /// A factory that receives the <see cref="ActorSystemImpl"/> and returns the <see cref="Props"/>
    /// used to create the logger actor.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="propsFactory"/> is null.</exception>
    public LoggerRegistration(Func<ActorSystemImpl, Props> propsFactory)
    {
        PropsFactory = propsFactory ?? throw new ArgumentNullException(nameof(propsFactory));
        LoggerType = null;
    }

    /// <summary>
    /// The logger type, if this is a type-based registration. <c>null</c> for factory-based registrations.
    /// </summary>
    public Type? LoggerType { get; }

    /// <summary>
    /// The props factory, if this is a factory-based registration. <c>null</c> for type-based registrations.
    /// </summary>
    public Func<ActorSystemImpl, Props>? PropsFactory { get; }

    /// <summary>
    /// INTERNAL API. Resolves the <see cref="Props"/> for this registration.
    /// </summary>
    internal Props CreateProps(ActorSystemImpl system)
    {
        if (PropsFactory != null)
            return PropsFactory(system);

        return Props.Create(LoggerType!);
    }

    /// <summary>
    /// INTERNAL API. Returns the effective logger type for naming and classification purposes.
    /// For factory-based registrations this walks the <see cref="Props"/> type.
    /// </summary>
    internal Type EffectiveType(ActorSystemImpl system)
    {
        if (LoggerType != null)
            return LoggerType;

        // For factory-based, ask the props what type it is
        return CreateProps(system).Type;
    }
}

/// <summary>
/// AOT-compatible setup class for registering custom loggers with an <see cref="ActorSystem"/>.
/// </summary>
/// <remarks>
/// <para>
/// In AOT (Ahead-Of-Time) compilation scenarios, HOCON-based custom logger configuration
/// is not supported because it relies on <see cref="Type.GetType(string)"/> which requires
/// dynamic type loading. Use <see cref="LoggerSetup"/> instead to register custom loggers
/// programmatically.
/// </para>
/// <para>
/// When a <see cref="LoggerSetup"/> is present in the <see cref="ActorSystemSetup"/>, its
/// registered loggers are used <em>instead of</em> the <c>akka.loggers</c> HOCON list.
/// </para>
/// <para>
/// This class follows the same pattern as <see cref="Akka.Serialization.SerializationSetup"/>
/// and is separate from <see cref="LogFilterSetup"/> following the Single Responsibility Principle.
/// </para>
/// </remarks>
/// <example>
/// Using type registration:
/// <code>
/// var loggerSetup = new LoggerSetupBuilder()
///     .AddLogger&lt;MyCustomLogger&gt;()
///     .AddLogger(typeof(AnotherLogger))
///     .Build();
///
/// var setup = ActorSystemSetup.Create(loggerSetup);
/// var system = ActorSystem.Create("MySystem", setup);
/// </code>
///
/// Combining with <see cref="LogFilterSetup"/>:
/// <code>
/// var setup = ActorSystemSetup.Create(
///     new LoggerSetupBuilder().AddLogger&lt;MyCustomLogger&gt;().Build(),
///     new LogFilterBuilder().ExcludeSourceContaining("Akka.Tests").Build());
/// </code>
/// </example>
public sealed class LoggerSetup : Setup
{
    internal LoggerSetup(IReadOnlyList<LoggerRegistration> loggers)
    {
        Loggers = loggers;
    }

    /// <summary>
    /// The ordered list of logger registrations.
    /// </summary>
    public IReadOnlyList<LoggerRegistration> Loggers { get; }
}

/// <summary>
/// Fluent builder for creating a <see cref="LoggerSetup"/>.
/// </summary>
/// <remarks>
/// Follows the same pattern as <see cref="LogFilterBuilder"/>.
/// </remarks>
/// <example>
/// <code>
/// var loggerSetup = new LoggerSetupBuilder()
///     .AddLogger&lt;MyCustomLogger&gt;()
///     .AddLogger(typeof(AnotherLogger))
///     .AddLogger(system =&gt; Props.Create(() =&gt; new SpecialLogger(system.Settings)))
///     .Build();
/// </code>
/// </example>
public sealed class LoggerSetupBuilder
{
    private readonly List<LoggerRegistration> _loggers = new();

    /// <summary>
    /// Adds a logger by its type using generic syntax.
    /// </summary>
    /// <typeparam name="T">The logger actor type. Must derive from <see cref="ActorBase"/>.</typeparam>
    /// <returns>This builder, for fluent chaining.</returns>
    public LoggerSetupBuilder AddLogger<T>() where T : ActorBase
    {
        _loggers.Add(new LoggerRegistration(typeof(T)));
        return this;
    }

    /// <summary>
    /// Adds a logger by its runtime type.
    /// </summary>
    /// <param name="loggerType">The logger actor type. Must derive from <see cref="ActorBase"/>.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="loggerType"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="loggerType"/> does not derive from <see cref="ActorBase"/>.</exception>
    public LoggerSetupBuilder AddLogger(Type loggerType)
    {
        if (loggerType == null) throw new ArgumentNullException(nameof(loggerType));
        if (!typeof(ActorBase).IsAssignableFrom(loggerType))
            throw new ArgumentException(
                $"Logger type '{loggerType.FullName}' must derive from ActorBase.",
                nameof(loggerType));

        _loggers.Add(new LoggerRegistration(loggerType));
        return this;
    }

    /// <summary>
    /// Adds a logger using a factory that receives the <see cref="ActorSystemImpl"/> and produces
    /// the <see cref="Props"/> to use when creating the logger actor. Useful when the logger
    /// requires access to system-level resources at construction time.
    /// </summary>
    /// <param name="propsFactory">Factory function from <see cref="ActorSystemImpl"/> to <see cref="Props"/>.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="propsFactory"/> is null.</exception>
    public LoggerSetupBuilder AddLogger(Func<ActorSystemImpl, Props> propsFactory)
    {
        if (propsFactory == null) throw new ArgumentNullException(nameof(propsFactory));
        _loggers.Add(new LoggerRegistration(propsFactory));
        return this;
    }

    /// <summary>
    /// Builds the <see cref="LoggerSetup"/> from the accumulated registrations.
    /// </summary>
    public LoggerSetup Build()
    {
        return new LoggerSetup(_loggers.AsReadOnly());
    }
}
