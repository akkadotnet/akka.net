// //-----------------------------------------------------------------------
// // <copyright file="Extensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Hocon
{
    internal static class Extensions
    {
        /// <summary>
        /// Splits a 'dotted path' in its elements, honouring quotes (not splitting by dots between quotes)
        /// </summary>
        /// <param name="path">The input path</param>
        /// <returns>The path elements</returns>
        public static IEnumerable<string> SplitDottedPathHonouringQuotes(this string path)
        {
            var i = 0;
            var j = 0;
            while (true)
            {
                if (j >= path.Length) yield break;
                else if (path[j] == '\"')
                {
                    i = path.IndexOf('\"', j + 1);
                    yield return path.Substring(j + 1, i - j - 1);
                    j = i + 2;
                }
                else
                {
                    i = path.IndexOf('.', j);
                    if (i == -1)
                    {
                        yield return path.Substring(j);
                        yield break;
                    }
                    yield return path.Substring(j, i - j);
                    j = i + 1;
                }
            }
        }        
    }
}