//-----------------------------------------------------------------------
// <copyright file="WarningFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class WarningFilter : EventFilterBase
    {
        public WarningFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : base(messageMatcher, sourceMatcher)
        {
        }
        protected override bool IsMatch(LogEvent evt)
        {
            var warning = evt as Warning;
            if(warning != null)
            {
                return InternalDoMatch(warning.LogSource, warning.Message);
            }
            return false;
        }

        protected override string FilterDescriptiveName { get { return "Warning"; } }
    }
}

