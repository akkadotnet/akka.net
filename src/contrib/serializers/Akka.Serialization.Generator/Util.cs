// -----------------------------------------------------------------------
// <copyright file="Util.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

namespace Akka.Serialization.Generator;

public static class Util
{
    internal static string ToBase26(this long i)
    {
        if (i == 0) return ""; 
        i--;
        return ToBase26(i / 26) + (char)('A' + i % 26);
    }
}