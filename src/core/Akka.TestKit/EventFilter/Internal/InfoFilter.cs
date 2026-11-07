//-----------------------------------------------------------------------
// <copyright file="InfoFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

#nullable enable
namespace Akka.TestKit.Internal;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class InfoFilter : EventFilterBase
{
    /// <summary>
    /// Creates a filter for info events, optionally matching their message and source.
    /// </summary>
    /// <param name="messageMatcher">Matches the event message, or null to match any message.</param>
    /// <param name="sourceMatcher">Matches the event source, or null to match any source.</param>
    public InfoFilter(IStringMatcher? messageMatcher = null, IStringMatcher? sourceMatcher = null)
        : base(messageMatcher, sourceMatcher)
    {
    }

    /// <summary>
    /// Checks whether the event is an info event that matches the configured source and message criteria.
    /// </summary>
    /// <param name="evt">The log event to inspect.</param>
    /// <returns><c>true</c> if the event matches; otherwise, <c>false</c>.</returns>
    protected override bool IsMatch(LogEvent evt)
    {
        var info = evt as Info;
        if(info != null)
        {
            return InternalDoMatch(info.LogSource, info.Message);
        }

        return false;
    }

    /// <summary>
    /// Gets the label used to describe this filter in diagnostics.
    /// </summary>
    protected override string FilterDescriptiveName { get { return "Info"; } }
}
