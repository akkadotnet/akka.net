//-----------------------------------------------------------------------
// <copyright file="EqualsString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class EqualsString : IStringMatcher
    {
        private readonly string _s;

        public EqualsString(string s)
        {
            _s = s;
        }

        public bool IsMatch(string s)
        {
            return String.Equals(_s, s, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return "== \"" + _s + "\"";
        }
    }
}

