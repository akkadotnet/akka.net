﻿//-----------------------------------------------------------------------
// <copyright file="MatchesAll.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class MatchesAll : IStringMatcher
    {
        private MatchesAll()
        {
        }

        public static IStringMatcher Instance { get; } = new MatchesAll();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "";
        }
    }
}
