//-----------------------------------------------------------------------
// <copyright file="IStringMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
namespace Akka.TestKit.Internal.StringMatcher;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public interface IStringMatcher
{
    /// <summary>
    /// Checks whether a string satisfies this matching criterion.
    /// </summary>
    /// <param name="s">The string to inspect.</param>
    /// <returns><c>true</c> if the string matches; otherwise, <c>false</c>.</returns>
    bool IsMatch(string s);
}
