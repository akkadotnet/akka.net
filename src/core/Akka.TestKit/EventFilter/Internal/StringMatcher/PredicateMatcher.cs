//-----------------------------------------------------------------------
// <copyright file="PredicateMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class PredicateMatcher : IStringMatcher
    {
        private readonly Predicate<string> _predicate;
        private readonly string _hint;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        /// <param name="hint">TBD</param>
        public PredicateMatcher(Predicate<string> predicate, string hint="")
        {
            _predicate = predicate;
            _hint = hint;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public bool IsMatch(string s)
        {
            return _predicate(s);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "matches predicate "+_hint;
        }
    }
}
