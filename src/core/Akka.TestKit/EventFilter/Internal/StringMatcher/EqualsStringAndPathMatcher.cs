//-----------------------------------------------------------------------
// <copyright file="EqualsStringAndPathMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

#nullable enable
namespace Akka.TestKit.Internal.StringMatcher;

/// <summary>
/// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class EqualsStringAndPathMatcher : IStringMatcher
{
    private readonly string _path;
    private readonly bool _canBeRelative;

    /// <summary>
    /// Creates a matcher for an exact string or, optionally, an actor path without its address.
    /// </summary>
    /// <param name="path">The string or addressless actor path to match.</param>
    /// <param name="canBeRelative">Whether a full actor path may also match by its addressless path.</param>
    public EqualsStringAndPathMatcher(string path, bool canBeRelative=true)
    {
        _path = path;
        _canBeRelative = canBeRelative;
    }

    /// <summary>
    /// Checks an exact string match and optionally compares parsed actor paths without their addresses.
    /// </summary>
    /// <param name="path">The string to compare.</param>
    /// <returns><c>true</c> if the string matches the configured value or accepted addressless path; otherwise, <c>false</c>.</returns>
    public bool IsMatch(string path)
    {
        if (String.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) return true;
        if(!_canBeRelative)return false;

        if (!ActorPath.TryParse(path, out var actorPath)) return false;
        var pathWithoutAddress = actorPath.ToStringWithoutAddress();
        return String.Equals(_path, pathWithoutAddress, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the configured path criterion in diagnostic form.
    /// </summary>
    /// <returns>A description of the path match.</returns>
    public override string ToString()
    {
        return "== \"" + _path + "\"";
    }
}
