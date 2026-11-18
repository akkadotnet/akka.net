//-----------------------------------------------------------------------
// <copyright file="MatchesAll.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
namespace Akka.TestKit.Internal.StringMatcher;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class MatchesAll : IStringMatcher
{
    private MatchesAll()
    {
    }

    public static IStringMatcher Instance { get; } = new MatchesAll();

    /// <summary>
    /// Matches every string.
    /// </summary>
    /// <param name="s">The string to inspect.</param>
    /// <returns>Always <c>true</c>.</returns>
    public bool IsMatch(string s)
    {
        return true;
    }

    /// <summary>
    /// Returns an empty description because this matcher adds no filtering condition.
    /// </summary>
    /// <returns>An empty string.</returns>
    public override string ToString()
    {
        return "";
    }
}
