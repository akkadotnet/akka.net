//-----------------------------------------------------------------------
// <copyright file="StartsWithString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="start">TBD</param>
        public StartsWithString(string start)
        {
            _start = start;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return s.StartsWith(_start, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "starts with \"" + _start + "\"";
        }
    }
}
