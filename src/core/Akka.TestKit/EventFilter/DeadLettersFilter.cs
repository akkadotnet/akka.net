//-----------------------------------------------------------------------
// <copyright file="DeadLettersFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit
{
    /// <summary>
    /// Filter which matches DeadLetter events, if the wrapped message conforms to the given type.
    /// </summary>
    public sealed class DeadLettersFilter : EventFilterBase
    {
        private readonly Predicate<DeadLetter> _isMatch;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageMatcher">TBD</param>
        /// <param name="sourceMatcher">TBD</param>
        /// <param name="isMatch">TBD</param>
        public DeadLettersFilter(IStringMatcher messageMatcher, IStringMatcher sourceMatcher, Predicate<DeadLetter> isMatch = null)
            : base(messageMatcher, sourceMatcher)
        {
            _isMatch = isMatch;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <returns>TBD</returns>
        protected override bool IsMatch(LogEvent evt)
        {
            var warning = evt as Warning;
            if(warning != null)
            {
                var deadLetter = warning.Message as DeadLetter;
                if(deadLetter != null)
                    if(_isMatch == null || _isMatch(deadLetter))
                        return InternalDoMatch(warning.LogSource, deadLetter.Message);
            }

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string FilterDescriptiveName { get { return "DeadLetter"; } }
    }
}
