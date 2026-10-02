// -----------------------------------------------------------------------
//  <copyright file="Utils.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Akka.Persistence.Hosting
{
    internal static class Utils
    {
        // This illegal character list is conservative. Normally '.' and '/' is allowed in a HOCON literal, but we'll
        // ban it just because it'll make our life harder in the future.
        private const string IllegalChars = "$\"{}[]:=,#`^?!@*&\\./";

        public static string[] IsIllegalHoconKey(this string s)
        {
            var illegals = new List<string>();
            foreach (var c in s)
            {
                if(IllegalChars.Contains(c))
                    illegals.Add($"{c}");
                else if(char.IsWhiteSpace(c))
                    illegals.Add($"\\u{(int)c:X4}");
            }

            return illegals.Distinct().ToArray();
        }
    }
}