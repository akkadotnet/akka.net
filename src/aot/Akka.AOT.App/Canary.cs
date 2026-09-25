//-----------------------------------------------------------------------
// <copyright file="Canary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.AOT.App;

/// <summary>
/// The one assertion helper shared by <see cref="Program"/> and <see cref="Scenarios"/>.
/// </summary>
internal static class Canary
{
    public static void Require(string label, bool condition, string problem)
    {
        if (!condition)
            throw new InvalidOperationException($"{label}: {problem}");
    }
}
