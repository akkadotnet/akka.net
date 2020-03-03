//-----------------------------------------------------------------------
// <copyright file="StringLike.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Akka.Util
{
    using System.Text;

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
        public static bool Like(this string text, string pattern, bool caseSensitive = false)
        {
            var sb = new StringBuilder("^");
            for (int index = 0; index < pattern.Length; index++)
            {
                var c = pattern[index];
                switch (c)
                {
                    case '.':
                        sb.Append(@"\.");
                        break;
                    case '?':
                        sb.Append('.');
                        break;
                    case '*':
                        sb.Append(".*?");
                        break;
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    case '$':
                        sb.Append(@"\$");
                        break;
                    case '^':
                        sb.Append(@"\^");
                        break;
                    case ' ':
                        sb.Append(@"\s");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            pattern = sb.Append('$').ToString();
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase).IsMatch(text);
        }

        #endregion
    }
}

