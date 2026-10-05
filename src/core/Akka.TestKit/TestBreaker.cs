//-----------------------------------------------------------------------
// <copyright file="TestBreaker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Exposes circuit-breaker state changes through waitable countdown events for tests.
    /// </summary>
    public class TestBreaker
    {
        /// <summary>
        /// Gets the latch signaled when the circuit breaker enters the half-open state.
        /// </summary>
        public CountdownEvent HalfOpenLatch { get; private set; }
        /// <summary>
        /// Gets the latch signaled when the circuit breaker opens.
        /// </summary>
        public CountdownEvent OpenLatch { get; private set; }
        /// <summary>
        /// Gets the latch signaled when the circuit breaker closes.
        /// </summary>
        public CountdownEvent ClosedLatch { get; private set; }
        /// <summary>
        /// Gets the circuit breaker observed by this test helper.
        /// </summary>
        public CircuitBreaker Instance { get; private set; }

        /// <summary>
        /// Creates a test helper and registers callbacks for the circuit breaker's state transitions.
        /// </summary>
        /// <param name="instance">The circuit breaker to observe.</param>
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
