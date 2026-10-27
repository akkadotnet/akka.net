//-----------------------------------------------------------------------
// <copyright file="ContainsString.cs" company="Akka.NET Project">
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
public class ContainsString : IStringMatcher
{
    private readonly string _part;

    /// <summary>
    /// Creates a matcher that looks for a case-insensitive substring.
    /// </summary>
    /// <param name="part">The substring to find.</param>
    public ContainsString(string part)
    {
        _part = part;
    }

    /// <summary>
    /// Checks whether a string contains the configured substring, ignoring case.
    /// </summary>
    /// <param name="s">The string to inspect.</param>
    /// <returns><c>true</c> if the string contains the substring; otherwise, <c>false</c>.</returns>
    public bool IsMatch(string s)
    {
        return s.IndexOf(_part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Returns the substring criterion in diagnostic form.
    /// </summary>
    /// <returns>A description of the substring match.</returns>
    public override string ToString()
    {
        return "contains \"" + _part + "\"";
    }
}
