//-----------------------------------------------------------------------
// <copyright file="IEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

#nullable enable
namespace Akka.TestKit;

// ReSharper disable once InconsistentNaming
/// <summary>
/// A predicate that determines whether a log event should be suppressed by a test event filter.
/// </summary>
public interface IEventFilter
{
    /// <summary>
    /// Applies this filter to a log event.
    /// </summary>
    /// <param name="logEvent">The log event to inspect.</param>
    /// <returns><c>true</c> if the event matches and should be filtered; otherwise, <c>false</c>.</returns>
    bool Apply(LogEvent logEvent);
}
