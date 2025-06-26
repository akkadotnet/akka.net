//-----------------------------------------------------------------------
// <copyright file="StringLike.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Akka.Util
{
    using System.Text;

    /// <summary>
    /// Provides wildcard pattern matching extensions for strings.
    /// </summary>
    public static class WildcardMatch
    {
        #region Public Methods

        /// <summary>
        /// Checks if a string matches a wildcard pattern using * for any sequence and ? for any single character.
        /// </summary>
        /// <param name="text">The text to check against the pattern.</param>
        /// <param name="pattern">The wildcard pattern to match with (* for any sequence, ? for any character).</param>
        /// <param name="caseSensitive">Whether the matching should be case sensitive. Default is false.</param>
        /// <returns>True if the text matches the pattern, false otherwise.</returns>
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

