//-----------------------------------------------------------------------
// <copyright file="RegexMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

#nullable enable
namespace Akka.TestKit.Internal.StringMatcher;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class RegexMatcher : IStringMatcher
{
    private readonly Regex _regex;

    /// <summary>
    /// Creates a matcher that uses a regular expression.
    /// </summary>
    /// <param name="regex">The regular expression to apply.</param>
    public RegexMatcher(Regex regex)
    {
        _regex = regex;
    }

    /// <summary>
    /// Checks whether the regular expression matches the string.
    /// </summary>
    /// <param name="s">The string to inspect.</param>
    /// <returns><c>true</c> if the regular expression matches; otherwise, <c>false</c>.</returns>
    public bool IsMatch(string s)
    {
        return _regex.IsMatch(s);
    }

    /// <summary>
    /// Returns the regular expression criterion in diagnostic form.
    /// </summary>
    /// <returns>A description of the regular expression match.</returns>
    public override string ToString()
    {
        return "matches regex \"" + _regex + "\"";
    }
}
