//-----------------------------------------------------------------------
// <copyright file="TestBreaker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TestBreaker
    {
        /// <summary>
        /// TBD
        /// </summary>
        public CountdownEvent HalfOpenLatch { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public CountdownEvent OpenLatch { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public CountdownEvent ClosedLatch { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public CircuitBreaker Instance { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="instance">TBD</param>
        public TestBreaker(CircuitBreaker instance)
        {
            HalfOpenLatch = new CountdownEvent(1);
            OpenLatch = new CountdownEvent(1);
            ClosedLatch = new CountdownEvent(1);
            Instance = instance;
            Instance.OnClose(() => { if (!ClosedLatch.IsSet) ClosedLatch.Signal(); })
                    .OnHalfOpen(() => { if (!HalfOpenLatch.IsSet) HalfOpenLatch.Signal(); })
                    .OnOpen(() => { if (!OpenLatch.IsSet) OpenLatch.Signal(); });
        }
    }
}
