//-----------------------------------------------------------------------
// <copyright file="StringLike.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Akka.Util
{
    public static class WildcardMatch
    {
        #region Public Methods
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

