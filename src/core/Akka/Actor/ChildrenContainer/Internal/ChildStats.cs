//-----------------------------------------------------------------------
// <copyright file="ChildStats.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IChildStats
    {
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ChildNameReserved : IChildStats
    {
        private ChildNameReserved() {/* Intentionally left blank */}

        /// <summary>
        /// TBD
        /// </summary>
        public static ChildNameReserved Instance { get; } = new ChildNameReserved();

        public override string ToString()
        {
            return "Name Reserved";
        }
    }

    /// <summary>
    /// ChildRestartStats is the statistics kept by every parent Actor for every child Actor
    /// and is used for SupervisorStrategies to know how to deal with problems that occur for the children.
    /// </summary>
    public class ChildRestartStats : IChildStats
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <param name="maxNrOfRetriesCount">TBD</param>
        /// <param name="restartTimeWindowStartTicks">TBD</param>
        public ChildRestartStats(IInternalActorRef child, uint maxNrOfRetriesCount = 0, long restartTimeWindowStartTicks = 0)
        {
            Child = child;
            MaxNrOfRetriesCount = maxNrOfRetriesCount;
            RestartTimeWindowStartTicks = restartTimeWindowStartTicks;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public long Uid { get { return Child.Path.Uid; } }

        /// <summary>
        /// TBD
        /// </summary>
        public IInternalActorRef Child { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public uint MaxNrOfRetriesCount { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public long RestartTimeWindowStartTicks { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxNrOfRetries">TBD</param>
        /// <param name="withinTimeMilliseconds">TBD</param>
        /// <returns>TBD</returns>
        public bool RequestRestartPermission(int maxNrOfRetries, int withinTimeMilliseconds)
        {
            if (maxNrOfRetries == 0) return false;
            var retriesIsDefined = maxNrOfRetries > 0;
            var windowIsDefined = withinTimeMilliseconds > 0;
            if (retriesIsDefined && !windowIsDefined)
            {
                MaxNrOfRetriesCount++;
                return MaxNrOfRetriesCount <= maxNrOfRetries;
            }
            if (windowIsDefined)
            {
                return RetriesInWindowOkay(retriesIsDefined ? maxNrOfRetries : 1, withinTimeMilliseconds);
            }
            return true;
            //retriesWindow match {
            //  case (Some(retries), _) if retries < 1 ⇒ false
            //  case (Some(retries), None)             ⇒ { maxNrOfRetriesCount += 1; maxNrOfRetriesCount <= retries }
            //  case (x, Some(window))                 ⇒ retriesInWindowOkay(if (x.isDefined) x.get else 1, window)
            //  case (None, _)                         ⇒ true
            //}
        }

        private bool RetriesInWindowOkay(int retries, int windowInMilliseconds)
        {
            // Simple window algorithm: window is kept open for a certain time
            // after a restart and if enough restarts happen during this time, it
            // denies. Otherwise window closes and the scheme starts over.
            var retriesDone = MaxNrOfRetriesCount + 1;
            var now = MonotonicClock.Elapsed.Ticks;
            long windowStart;
            if (RestartTimeWindowStartTicks == 0)
            {
                RestartTimeWindowStartTicks = now;
                windowStart = now;
            }
            else
            {
                windowStart = RestartTimeWindowStartTicks;
            }
            var windowInTicks = windowInMilliseconds * TimeSpan.TicksPerMillisecond;
            var insideWindow = (now - windowStart) <= windowInTicks;

            if (insideWindow)
            {
                MaxNrOfRetriesCount = retriesDone;
                return retriesDone <= retries;
            }
            MaxNrOfRetriesCount = 1;
            RestartTimeWindowStartTicks = now;
            return true;
        }
    }
}
