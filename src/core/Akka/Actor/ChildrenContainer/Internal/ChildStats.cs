//-----------------------------------------------------------------------
// <copyright file="ChildStats.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Marker interface for a child entry or reserved child name.
    /// </summary>
    public interface IChildStats
    {
    }

    /// <summary>
    /// Marks an actor name as reserved while its child reference is being created.
    /// </summary>
    public class ChildNameReserved : IChildStats
    {
        private ChildNameReserved() {/* Intentionally left blank */}

        /// <summary>
        /// The shared marker used for reserved child names.
        /// </summary>
        public static ChildNameReserved Instance { get; } = new();

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
        private readonly IInternalActorRef _child;
        private uint _maxNrOfRetriesCount;
        private long _restartTimeWindowStartTicks;

        /// <summary>
        /// Creates restart statistics for a child actor.
        /// </summary>
        /// <param name="child">The child reference being tracked.</param>
        /// <param name="maxNrOfRetriesCount">The number of restart attempts already counted.</param>
        /// <param name="restartTimeWindowStartTicks">The monotonic-clock tick when the current retry window began, or zero when no window is active.</param>
        public ChildRestartStats(IInternalActorRef child, uint maxNrOfRetriesCount = 0, long restartTimeWindowStartTicks = 0)
        {
            _child = child;
            _maxNrOfRetriesCount = maxNrOfRetriesCount;
            _restartTimeWindowStartTicks = restartTimeWindowStartTicks;
        }

        /// <summary>
        /// The unique identifier from the child's actor path.
        /// </summary>
        public long Uid { get { return Child.Path.Uid; } }

        /// <summary>
        /// The child actor reference being tracked.
        /// </summary>
        public IInternalActorRef Child { get { return _child; } }

        /// <summary>
        /// The number of restart attempts counted in the current retry window.
        /// </summary>
        public uint MaxNrOfRetriesCount { get { return _maxNrOfRetriesCount; } }

        /// <summary>
        /// The monotonic-clock tick when the current retry window began.
        /// </summary>
        public long RestartTimeWindowStartTicks { get { return _restartTimeWindowStartTicks; } }

        /// <summary>
        /// Records a restart attempt and checks whether the strategy's retry limit permits it.
        /// </summary>
        /// <param name="maxNrOfRetries">The maximum number of retries; zero permits none. A negative value permits one retry per positive time window, or has no count limit when there is no positive window.</param>
        /// <param name="withinTimeMilliseconds">The retry window in milliseconds, or a nonpositive value for no time window.</param>
        /// <returns><c>true</c> if another restart is permitted; otherwise, <c>false</c>.</returns>
        public bool RequestRestartPermission(int maxNrOfRetries, int withinTimeMilliseconds)
        {
            if (maxNrOfRetries == 0) return false;
            var retriesIsDefined = maxNrOfRetries > 0;
            var windowIsDefined = withinTimeMilliseconds > 0;
            if (retriesIsDefined && !windowIsDefined)
            {
                _maxNrOfRetriesCount++;
                return _maxNrOfRetriesCount <= maxNrOfRetries;
            }
            if (windowIsDefined)
            {
                return RetriesInWindowOkay(retriesIsDefined ? maxNrOfRetries : 1, withinTimeMilliseconds);
            }
            return true;
        }

        private bool RetriesInWindowOkay(int retries, int windowInMilliseconds)
        {
            // Simple window algorithm: window is kept open for a certain time
            // after a restart and if enough restarts happen during this time, it
            // denies. Otherwise window closes and the scheme starts over.
            var retriesDone = _maxNrOfRetriesCount + 1;
            var now = MonotonicClock.Elapsed.Ticks;
            long windowStart;
            if (_restartTimeWindowStartTicks == 0)
            {
                _restartTimeWindowStartTicks = now;
                windowStart = now;
            }
            else
            {
                windowStart = _restartTimeWindowStartTicks;
            }
            var windowInTicks = windowInMilliseconds * TimeSpan.TicksPerMillisecond;
            var insideWindow = (now - windowStart) <= windowInTicks;

            if (insideWindow)
            {
                _maxNrOfRetriesCount = retriesDone;
                return retriesDone <= retries;
            }
            _maxNrOfRetriesCount = 1;
            _restartTimeWindowStartTicks = now;
            return true;
        }
    }
}
