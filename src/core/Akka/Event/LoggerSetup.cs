//-----------------------------------------------------------------------
// <copyright file="LoggerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Setup;

namespace Akka.Event;

/// <summary>
/// Loggers to start with the <see cref="ActorSystem"/>, the programmatic equivalent of
/// <c>akka.loggers</c> that needs no type names - the AOT-safe alternative, since HOCON-configured
/// loggers are resolved with <see cref="Type.GetType(string)"/>. Each entry is the <see cref="Props"/>
/// for an actor that handles <see cref="InitializeLogger"/> the way <see cref="LoggingBus"/> expects
/// any logger to (see <see cref="LoggingBus.StartDefaultLoggers"/>); build one with
/// <see cref="Akka.Actor.Props.Create{TActor}(object[])"/> against your logger's own type.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="LoggerSetup"/> is additive to <c>akka.loggers</c>, not a replacement for it: both
/// start, and a logger type named by both starts once. The default <c>akka.loggers = [DefaultLogger]</c>
/// keeps printing to standard output alongside the loggers registered here unless you also set
/// <c>akka.loggers = []</c>.
/// </para>
/// <para>
/// Like any <see cref="Setup"/>, a second <see cref="LoggerSetup"/> passed to
/// <see cref="ActorSystemSetup.And{T}"/> replaces the first - only one <see cref="LoggerSetup"/> exists
/// per system.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var setup = ActorSystemSetup.Create(LoggerSetup.Create(Props.Create&lt;MyCustomLogger&gt;()));
/// var system = ActorSystem.Create("MySystem", setup);
/// </code>
/// </example>
public sealed class LoggerSetup : Setup
{
    private LoggerSetup(IList<Props> loggers)
    {
        Loggers = new ReadOnlyCollection<Props>(loggers);
    }

    /// <summary>
    /// The logger <see cref="Props"/> to start when the <see cref="ActorSystem"/> starts.
    /// </summary>
    public IReadOnlyList<Props> Loggers { get; }

    /// <summary>
    /// Creates a <see cref="LoggerSetup"/> that starts the given loggers.
    /// </summary>
    /// <param name="loggers">The logger <see cref="Props"/> to start when the <see cref="ActorSystem"/> starts.</param>
    /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
    public static LoggerSetup Create(IEnumerable<Props> loggers)
    {
        var list = loggers.ToArray();
        if (list.Any(p => p is null))
            throw new ArgumentException("Logger Props must not be null.", nameof(loggers));
        return new LoggerSetup(list);
    }

    /// <summary>
    /// Creates a <see cref="LoggerSetup"/> that starts the given loggers.
    /// </summary>
    /// <param name="loggers">The logger <see cref="Props"/> to start when the <see cref="ActorSystem"/> starts.</param>
    /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
    public static LoggerSetup Create(params Props[] loggers)
        => Create((IEnumerable<Props>)loggers);
}
