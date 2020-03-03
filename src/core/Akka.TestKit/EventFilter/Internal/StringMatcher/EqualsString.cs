//-----------------------------------------------------------------------
// <copyright file="EqualsString.cs" company="Akka.NET Project">
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
    public class EqualsString : IStringMatcher
    {
        private readonly string _s;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        public EqualsString(string s)
        {
            _s = s;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return String.Equals(_s, s, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "== \"" + _s + "\"";
        }
    }
}
