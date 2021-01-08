//-----------------------------------------------------------------------
// <copyright file="ContainsString.cs" company="Akka.NET Project">
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
    public class ContainsString : IStringMatcher
    {
        private readonly string _part;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="part">TBD</param>
        public ContainsString(string part)
        {
            _part = part;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return s.IndexOf(_part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "contains \"" + _part + "\"";
        }
    }
}
