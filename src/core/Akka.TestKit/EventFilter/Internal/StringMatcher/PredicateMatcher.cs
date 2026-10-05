//-----------------------------------------------------------------------
// <copyright file="PredicateMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

#nullable enable
namespace Akka.TestKit.Internal.StringMatcher;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class PredicateMatcher : IStringMatcher
{
    private readonly Predicate<string> _predicate;
    private readonly string _hint;

    /// <summary>
    /// Creates a matcher that delegates string checks to a predicate.
    /// </summary>
    /// <param name="predicate">The predicate that decides whether a string matches.</param>
    /// <param name="hint">Optional text included in the diagnostic description.</param>
    public PredicateMatcher(Predicate<string> predicate, string hint="")
    {
        _predicate = predicate;
        _hint = hint;
    }

    /// <summary>
    /// Evaluates the configured predicate for the string.
    /// </summary>
    /// <param name="s">The string to inspect.</param>
    /// <returns>The result of the predicate.</returns>
    public bool IsMatch(string s)
    {
        return _predicate(s);
    }

    /// <summary>
    /// Returns the predicate hint in diagnostic form.
    /// </summary>
    /// <returns>A description containing the configured hint.</returns>
    public override string ToString()
    {
        return "matches predicate "+_hint;
    }
}
