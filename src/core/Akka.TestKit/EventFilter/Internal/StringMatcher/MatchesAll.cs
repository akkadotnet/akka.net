//-----------------------------------------------------------------------
// <copyright file="MatchesAll.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class MatchesAll : IStringMatcher
    {
        private static readonly IStringMatcher _instance = new MatchesAll();

        private MatchesAll()
        {
        }

        public static IStringMatcher Instance { get { return _instance; } }

        public bool IsMatch(string s)
        {
            return true;
        }


        public override string ToString()
        {
            return "";
        }
    }
}

