//-----------------------------------------------------------------------
// <copyright file="DeadLettersFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public DeadLettersFilter(IStringMatcher messageMatcher, IStringMatcher sourceMatcher, Predicate<DeadLetter> isMatch = null)
            : base(messageMatcher, sourceMatcher)
        {
            _isMatch = isMatch;
        }

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

        protected override string FilterDescriptiveName { get { return "DeadLetter"; } }
    }
}
