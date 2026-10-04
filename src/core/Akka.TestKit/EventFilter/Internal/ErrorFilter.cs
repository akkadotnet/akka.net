//-----------------------------------------------------------------------
// <copyright file="ErrorFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;
using Akka.Util;

#nullable enable
namespace Akka.TestKit.Internal;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class ErrorFilter : EventFilterBase
{
    private readonly Type? _exceptionType;
    private readonly bool _recurseInnerExceptions;

    /// <summary>
    /// Creates a filter for error events without requiring a particular exception type.
    /// </summary>
    /// <param name="messageMatcher">Matches the event or exception message, or null to match any message.</param>
    /// <param name="sourceMatcher">Matches the event source, or null to match any source.</param>
    public ErrorFilter(IStringMatcher? messageMatcher = null, IStringMatcher? sourceMatcher = null)
        : this(null, messageMatcher, sourceMatcher, false)
    {
    }

    /// <summary>
    /// Creates a filter for error events, optionally matching the cause type, messages, and source.
    /// </summary>
    /// <param name="exceptionType">The required cause type, or null to match errors without a required type.</param>
    /// <param name="messageMatcher">Matches the event or cause message, or null to match any message.</param>
    /// <param name="sourceMatcher">Matches the event source, or null to match any source.</param>
    /// <param name="recurseInnerExceptions">Whether to search inner exceptions when matching the cause type or message.</param>
    /// <exception cref="ArgumentException">
    /// This exception is thrown when the specified <paramref name="exceptionType"/> does not implement <see cref="Exception"/>.
    /// </exception>
    public ErrorFilter(Type? exceptionType, IStringMatcher? messageMatcher = null, IStringMatcher? sourceMatcher = null, bool recurseInnerExceptions = false)
        : base(messageMatcher,sourceMatcher)
    {
        if(exceptionType is not null && !exceptionType.Implements<Exception>()) 
            throw new ArgumentException($"The type must be an exception. It was: {exceptionType}", nameof(exceptionType));
        _exceptionType = exceptionType;
        _recurseInnerExceptions = recurseInnerExceptions;
    }

    /// <summary>
    /// Matches error events whose cause type and message satisfy the configured criteria.
    /// </summary>
    /// <param name="evt">The log event to inspect.</param>
    /// <returns><c>true</c> if the event is a matching error; otherwise, <c>false</c>.</returns>
    protected override bool IsMatch(LogEvent evt)
    {
        if(evt is Error error)
        {
            var logSource = error.LogSource;
            var errorMessage = error.Message;
            var cause = error.Cause;
            return IsMatch(logSource, errorMessage, cause);
        }

        return false;
    }

    private bool IsMatch(string logSource, object? errorMessage, Exception? cause)
    {
        if (_exceptionType is null)
        {
            if (cause is null)
            {
                return InternalDoMatch(logSource, errorMessage);
            }
        }
        else
        {
            //Must match type. If no cause, then it does not match the specified exception type
            if (cause is null)
            {
                return false;
            }
            if (!cause.GetType().Implements(_exceptionType))
            {
                //The cause did not implement the specified type.
                if (_recurseInnerExceptions)
                    return IsMatch(logSource, errorMessage, cause.InnerException);
                return false;
            }
        }
        //At this stage we have a cause, and it's of the specified type.
        //Check that message matches, if nothing matches, recurse
        var causeMessage = cause.Message;
        var noMessages = (errorMessage == null && string.IsNullOrEmpty(causeMessage));
        return noMessages
               || InternalDoMatch(logSource, errorMessage)
               || InternalDoMatch(logSource, causeMessage)
               || (_recurseInnerExceptions && IsMatch(logSource, errorMessage, cause.InnerException));
    }

    /// <summary>
    /// Gets a diagnostic label that includes the required cause type when one is configured.
    /// </summary>
    protected override string FilterDescriptiveName
    {
        get
        {
            if(_exceptionType == null)
                return "Error";
            return "Error with Cause <" + _exceptionType + ">";
        }
    }
}
