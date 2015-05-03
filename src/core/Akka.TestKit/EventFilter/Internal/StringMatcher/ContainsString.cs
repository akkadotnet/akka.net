﻿//-----------------------------------------------------------------------
// <copyright file="ContainsString.cs" company="Akka.NET Project">
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
    public class ContainsString : IStringMatcher
    {
        private readonly string _part;

        public ContainsString(string part)
        {
            _part = part;
        }

        public bool IsMatch(string s)
        {
            return s.IndexOf(_part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override string ToString()
        {
            return "contains \"" + _part + "\"";
        }
    }
}

