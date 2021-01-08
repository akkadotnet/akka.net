//-----------------------------------------------------------------------
// <copyright file="CustomEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class CustomEventFilter : EventFilterBase
    {
        private readonly Predicate<LogEvent> _predicate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        public CustomEventFilter(Predicate<LogEvent> predicate)
            : base(null, null)
        {
            _predicate = predicate;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <returns>TBD</returns>
        protected override bool IsMatch(LogEvent evt)
        {
            return _predicate(evt);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string FilterDescriptiveName { get { return "Custom"; } }
    }
}
