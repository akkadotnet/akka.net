//-----------------------------------------------------------------------
// <copyright file="Unmute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit.TestEvent
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class Unmute : INoSerializationVerificationNeeded
    {
        private readonly IReadOnlyCollection<EventFilterBase> _filters;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="filters">TBD</param>
        public Unmute(params EventFilterBase[] filters)
        {
            _filters = filters;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="filters">TBD</param>
        public Unmute(IReadOnlyCollection<EventFilterBase> filters)
        {
            _filters = filters;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
    }
}
