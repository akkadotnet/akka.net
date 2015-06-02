﻿//-----------------------------------------------------------------------
// <copyright file="ChildStats.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Actor.Internal
{
    public interface IChildStats
    {
    }

    public class ChildNameReserved : IChildStats
    {
        private static readonly ChildNameReserved _instance = new ChildNameReserved();
        private ChildNameReserved() {/* Intentionally left blank */}

        public static ChildNameReserved Instance { get { return _instance; } }
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

        public ChildRestartStats(IInternalActorRef child, uint maxNrOfRetriesCount = 0, long restartTimeWindowStartTicks = 0)
        {
            _child = child;
            _maxNrOfRetriesCount = maxNrOfRetriesCount;
            _restartTimeWindowStartTicks = restartTimeWindowStartTicks;
        }

        public long Uid { get { return Child.Path.Uid; } }

        public IInternalActorRef Child { get { return _child; } }

        public uint MaxNrOfRetriesCount { get { return _maxNrOfRetriesCount; } }

        public long RestartTimeWindowStartTicks { get { return _restartTimeWindowStartTicks; } }

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

