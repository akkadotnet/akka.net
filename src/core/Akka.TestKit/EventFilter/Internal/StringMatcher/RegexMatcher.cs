//-----------------------------------------------------------------------
// <copyright file="RegexMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public RegexMatcher(Regex regex)
        {
            _regex = regex;
        }

        public bool IsMatch(string s)
        {
            return _regex.IsMatch(s);
        }


        public override string ToString()
        {
            return "matches regex \"" + _regex + "\"";
        }
    }
}

