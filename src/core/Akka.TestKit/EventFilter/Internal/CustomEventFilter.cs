//-----------------------------------------------------------------------
// <copyright file="CustomEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

#nullable enable
namespace Akka.TestKit.Internal;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class CustomEventFilter : EventFilterBase
{
    private readonly Predicate<LogEvent> _predicate;

    /// <summary>
    /// Creates a custom filter that evaluates each log event with the supplied predicate.
    /// </summary>
    /// <param name="predicate">Returns true for log events that this filter should suppress.</param>
    public CustomEventFilter(Predicate<LogEvent> predicate)
        : base(null, null)
    {
        _predicate = predicate;
    }

    /// <summary>
    /// Evaluates the predicate for the specified event.
    /// </summary>
    /// <param name="evt">The log event to inspect.</param>
    /// <returns>The result of the configured predicate.</returns>
    protected override bool IsMatch(LogEvent evt)
    {
        return _predicate(evt);
    }

    /// <summary>
    /// Gets the label used to describe this filter in diagnostics.
    /// </summary>
    protected override string FilterDescriptiveName { get { return "Custom"; } }
}
