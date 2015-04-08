//-----------------------------------------------------------------------
// <copyright file="StartsWithString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class StartsWithString : IStringMatcher
    {
        private readonly string _start;

        public StartsWithString(string start)
        {
            _start = start;
        }

        public bool IsMatch(string s)
        {
            return s.StartsWith(_start, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return "starts with \"" + _start + "\"";
        }
    }
}
