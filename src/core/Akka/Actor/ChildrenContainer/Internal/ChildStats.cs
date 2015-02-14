using System;

namespace Akka.Actor.Internal
{
    // ReSharper disable once InconsistentNaming
    public interface ChildStats
    {
    }

    public class ChildNameReserved : ChildStats
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
    public class ChildRestartStats : ChildStats
    {
        private readonly InternalActorRef _child;
        private uint _maxNrOfRetriesCount;
        private long _restartTimeWindowStartTicks;

        public ChildRestartStats(InternalActorRef child, uint maxNrOfRetriesCount = 0, long restartTimeWindowStartTicks = 0)
        {
            _child = child;
            _maxNrOfRetriesCount = maxNrOfRetriesCount;
            _restartTimeWindowStartTicks = restartTimeWindowStartTicks;
        }

        public long Uid { get { return Child.Path.Uid; } }

        public InternalActorRef Child { get { return _child; } }

        public uint MaxNrOfRetriesCount { get { return _maxNrOfRetriesCount; } }

        public long RestartTimeWindowStartTicks { get { return _restartTimeWindowStartTicks; } }

        public bool RequestRestartPermission(int maxNrOfRetries, int withinTimeMilliseconds)
        {
            if (maxNrOfRetries == 0) return false;
            if (withinTimeMilliseconds == 0)
            {
                _maxNrOfRetriesCount++;
                return _maxNrOfRetriesCount <= maxNrOfRetries;
            }
            return RetriesInWindowOkay(maxNrOfRetries, withinTimeMilliseconds);
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
            var now = DateTime.Now.Ticks;
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
            var windowInTicks = windowInMilliseconds*TimeSpan.TicksPerMillisecond;
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