//-----------------------------------------------------------------------
// <copyright file="DeadLettersFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;

#nullable enable
namespace Akka.TestKit;

/// <summary>
/// Filter which matches DeadLetter events, if the wrapped message conforms to the given type.
/// </summary>
public sealed class DeadLettersFilter : EventFilterBase
{
    private readonly Predicate<DeadLetter>? _isMatch;

    /// <summary>
    /// Creates a filter for dead letters with optional message, source, and dead-letter predicates.
    /// </summary>
    /// <param name="messageMatcher">Matches the dead-letter message text, or null to match any message.</param>
    /// <param name="sourceMatcher">Matches the log source, or null to match any source.</param>
    /// <param name="isMatch">An additional predicate for the dead-letter envelope, or null to accept all envelopes.</param>
    public DeadLettersFilter(IStringMatcher? messageMatcher, IStringMatcher? sourceMatcher, Predicate<DeadLetter>? isMatch = null)
        : base(messageMatcher, sourceMatcher)
    {
        _isMatch = isMatch;
    }

    /// <summary>
    /// Matches warning events that contain a dead-letter envelope satisfying the configured predicates.
    /// </summary>
    /// <param name="evt">The log event to inspect.</param>
    /// <returns><c>true</c> if the event is a matching dead-letter warning; otherwise, <c>false</c>.</returns>
    protected override bool IsMatch(LogEvent evt)
    {
        if(evt is Warning warning)
        {
            if(warning.Message is DeadLetter deadLetter)
                if(_isMatch == null || _isMatch(deadLetter))
                    return InternalDoMatch(warning.LogSource, deadLetter.Message);
        }

        return false;
    }

    /// <summary>
    /// Gets the label used to describe this filter in diagnostics.
    /// </summary>
    protected override string FilterDescriptiveName { get { return "DeadLetter"; } }
}
