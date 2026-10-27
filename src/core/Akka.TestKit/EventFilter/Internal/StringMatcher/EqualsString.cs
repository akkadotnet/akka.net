//-----------------------------------------------------------------------
// <copyright file="EqualsString.cs" company="Akka.NET Project">
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
public class EqualsString : IStringMatcher
{
    private readonly string _s;

    /// <summary>
    /// Creates a matcher that compares strings without regard to case.
    /// </summary>
    /// <param name="s">The string to match.</param>
    public EqualsString(string s)
    {
        _s = s;
    }

    /// <summary>
    /// Checks whether the string equals the configured value, ignoring case.
    /// </summary>
    /// <param name="s">The string to compare.</param>
    /// <returns><c>true</c> if the strings are equal; otherwise, <c>false</c>.</returns>
    public bool IsMatch(string s)
    {
        return String.Equals(_s, s, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the equality criterion in diagnostic form.
    /// </summary>
    /// <returns>A description of the exact string match.</returns>
    public override string ToString()
    {
        return "== \"" + _s + "\"";
    }
}
