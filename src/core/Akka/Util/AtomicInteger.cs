using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// A wrapper for an integer that contains thread-safe atomicity constructs.
    /// </summary>
    public class AtomicInteger
    {
        /// <summary>
        /// Creates a new instance of an AtomicInteger.
        /// </summary>
        /// <param name="seed"></param>
        public AtomicInteger(int seed = -1)
        {
            value = seed;
        }

        /// <summary>
        /// The current value of this integer.
        /// </summary>
        private int value = -1;

        /// <summary>
        /// A spinner used during CAS loops.
        /// </summary>
        private SpinWait spinner = new SpinWait();

        /// <summary>
        /// The current value of this integer.
        /// </summary>
        public int Value
        {
            get
            {
                return value;
            }
        }

        /// <summary>
        /// Gets the current value of the integer and increments it by one.
        /// </summary>
        /// <returns>The pre-incremented value.</returns>
        public int GetAndIncrement()
        {
            return GetAndAdd(1);
        }

        /// <summary>
        /// Gets the current value of the integer and adds the specified value to it.
        /// </summary>
        /// <param name="amount">The amount to add to the current value.</param>
        /// <returns>The pre-added value.</returns>
        public int GetAndAdd(int amount)
        {
            while(true)
            {
                var currentValue = value;
                if(Interlocked.CompareExchange(ref value, currentValue + amount, currentValue) == currentValue)
                {
                    return currentValue;
                }
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// Sets the integer to a new value.
        /// </summary>
        /// <param name="newValue">The value to set to.</param>
        public void Set(int newValue)
        {
            Interlocked.Exchange(ref this.value, newValue);
        }
    }
}
