using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// Class used for atomic counters and increments.
    /// 
    /// Used inside the <see cref="FSM{TS,TD}"/> and in parts of Akka.Remote.
    /// </summary>
    public class AtomicCounter
    {
        public AtomicCounter(int seed)
        {
            _seed = seed;
        }

        private int _seed;

        /// <summary>
        /// Retrieves the current value of the counter
        /// </summary>
        public int Current { get { return _seed; } }

        /// <summary>
        /// Increments the counter and returns the next value
        /// </summary>
        public int Next
        {
            get
            {
                return Interlocked.Increment(ref _seed);
            }
        }

        /// <summary>
        /// Returns the current value while simultaneously incrementing the counter
        /// </summary>
        public int GetAndIncrement()
        {
            var rValue = Current;
            var nextValue = Next;
            return rValue;
        }
    }
}
