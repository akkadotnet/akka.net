//-----------------------------------------------------------------------
// <copyright file="EventFilterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;
using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

#nullable enable
namespace Akka.TestKit.Internal;

/// <summary>
/// Base class for filters that match log events by their content and source.
/// </summary>
/// <param name="eventFilter">The filter that matched the event.</param>
/// <param name="logEvent">The matching log event.</param>
public delegate void EventMatched(EventFilterBase eventFilter, LogEvent logEvent);

/// <summary>Internal! 
/// Facilities for selectively filtering out expected events from logging so
/// that you can keep your test run’s console output clean and do not miss real
/// error messages.
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public abstract class EventFilterBase : IEventFilter
{
    private readonly IStringMatcher _sourceMatcher;
    private readonly IStringMatcher _messageMatcher;

    /// <summary>
    /// Initializes the message and source matchers used by the filter.
    /// </summary>
    /// <param name="messageMatcher">Matches the event message, or null to match any message.</param>
    /// <param name="sourceMatcher">Matches the event source, or null to match any source.</param>
    protected EventFilterBase(IStringMatcher? messageMatcher, IStringMatcher? sourceMatcher)
    {
        _messageMatcher = messageMatcher ?? MatchesAll.Instance;
        _sourceMatcher = sourceMatcher ?? MatchesAll.Instance;
    }

    /// <summary>
    /// Raised after this filter matches and suppresses a log event.
    /// </summary>
    public event EventMatched? EventMatched;

    /// <summary>
    /// Determines whether the specified event should be filtered or not.
    /// </summary>
    /// <param name="evt">The log event to inspect.</param>
    /// <returns><c>true</c> to filter the event.</returns>
    protected abstract bool IsMatch(LogEvent evt);  //In Akka JVM this is called matches

    /// <summary>
    /// Applies this filter and raises <see cref="EventMatched"/> when the event matches.
    /// </summary>
    /// <param name="logEvent">The log event to inspect.</param>
    /// <returns><c>true</c> if the event matches and should be filtered; otherwise, <c>false</c>.</returns>
    public bool Apply(LogEvent logEvent)
    {
        if(IsMatch(logEvent))
        {
            OnEventMatched(logEvent);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Notifies subscribers that this filter matched a log event.
    /// </summary>
    /// <param name="logEvent">The matching log event.</param>
    protected virtual void OnEventMatched(LogEvent logEvent)
    {
        var delegt = EventMatched;
        if(delegt != null) delegt(this, logEvent);
    }

    /// <summary>Internal helper.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <param name="src">The source associated with the event.</param>
    /// <param name="msg">The event message or message object to match.</param>
    /// <returns><c>true</c> if both configured matchers accept the source and message; otherwise, <c>false</c>.</returns>
    protected bool InternalDoMatch(string src, object? msg)
    {
        // Check source matcher first (fast path)
        if (!_sourceMatcher.IsMatch(src))
            return false;

        // For semantic logging support, try matching against both the formatted message
        // and the unformatted template pattern
        if (msg is LogMessage logMessage)
        {
            // Try matching against the template pattern first (e.g., "User {UserId} logged in")
            if (_messageMatcher.IsMatch(logMessage.Format))
                return true;

            // Fall back to matching the formatted message (e.g., "User 12345 logged in")
            var formattedMsg = logMessage.ToString() ?? "null";
            return _messageMatcher.IsMatch(formattedMsg);
        }

        // Non-semantic logging or legacy messages
        var msgstr = msg == null ? "null" : msg.ToString() ?? "null";
        return _messageMatcher.IsMatch(msgstr);
    }

    /// <summary>
    /// Gets the short name used to identify this filter in diagnostics.
    /// </summary>
    protected abstract string FilterDescriptiveName { get; }

    /// <summary>
    /// Returns a description of this filter and its configured matchers.
    /// </summary>
    /// <returns>The filter name and any message or source match criteria.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        //if(_occurences > 1)
        //    sb.Append(_occurences == int.MaxValue ? "infinite" : _occurences.ToString(CultureInfo.InvariantCulture)).Append(" occurences of ");
        sb.Append(FilterDescriptiveName);
        var hasMessageMatcher = !(_messageMatcher is MatchesAll);
        var hasSourceMatcher = !(_sourceMatcher is MatchesAll);
        var hasBothMessageAndSourceMatcher = hasMessageMatcher && hasSourceMatcher;
        if(hasMessageMatcher || hasSourceMatcher)
        {
            sb.Append(" when");
        }
        if(hasMessageMatcher)
        {
            sb.Append(" Message ");
            sb.Append(_messageMatcher);
        }
        if(hasBothMessageAndSourceMatcher)
        {
            sb.Append(" and");
        }
        if(hasSourceMatcher)
        {
            sb.Append(" Source ");
            sb.Append(_sourceMatcher);
        }
        return sb.ToString();
    }
}
