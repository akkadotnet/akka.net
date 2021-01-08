//-----------------------------------------------------------------------
// <copyright file="RegexMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class RegexMatcher : IStringMatcher
    {
        private readonly Regex _regex;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="regex">TBD</param>
        public RegexMatcher(Regex regex)
        {
            _regex = regex;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return _regex.IsMatch(s);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "matches regex \"" + _regex + "\"";
        }
    }
}
