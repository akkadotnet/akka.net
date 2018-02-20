//-----------------------------------------------------------------------
// <copyright file="StringLike.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class WildcardMatch
    {
        #region Public Methods
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="text">TBD</param>
        /// <param name="pattern">TBD</param>
        /// <param name="caseSensitive">TBD</param>
        /// <returns>TBD</returns>
        public static bool Like(this string text,string pattern, bool caseSensitive = false)
        {
            pattern = pattern.Replace(".", @"\.");
            pattern = pattern.Replace("?", ".");
            pattern = pattern.Replace("*", ".*?");
            pattern = pattern.Replace(@"\", @"\\");
            pattern = pattern.Replace(" ", @"\s");
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase).IsMatch(text);
        }
        #endregion
    }
}

