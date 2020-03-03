//-----------------------------------------------------------------------
// <copyright file="DebugFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class DebugFilter : EventFilterBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageMatcher">TBD</param>
        /// <param name="sourceMatcher">TBD</param>
        public DebugFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : base(messageMatcher,sourceMatcher)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <returns>TBD</returns>
        protected override bool IsMatch(LogEvent evt)
        {
            var debug = evt as Debug;
            if(debug != null)
            {
                return InternalDoMatch(debug.LogSource, debug.Message);
            }

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string FilterDescriptiveName { get { return "Debug"; } }
    }
}
