using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util.Internal
{
    /// <summary>
    /// An atomic 64 bit integer counter.
    /// </summary>
    public class AtomicCounterLong : IAtomicCounter<long>
    {
        /// <summary>
        /// Creates an instance of an AtomicCounterLong.
        /// </summary>
        /// <param name="value">The initial value of this counter.</param>
        public AtomicCounterLong(long value)
        {
            _value = value;
        }

        /// <summary>
        /// Creates an instance of an AtomicCounterLong with a starting value of -1.
        /// </summary>
        public AtomicCounterLong()
        {
            _value = -1;
        }

        /// <summary>
        /// The current value for this counter.
        /// </summary>
        private long _value;

        /// <summary>
        /// A spinner used during CAS loops.
        /// </summary>
        private SpinWait spinner = new SpinWait();

        /// <summary>
        /// Retrieves the current value of the counter
        /// </summary>
        public long Current { get { return Interlocked.Read(ref _value); } }

        /// <summary>
        /// Increments the counter and returns the next value.
        /// </summary>
        public long Next
        {
            get
            {
                return AddAndGet(1);
            }
        }

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The original value.</returns>
        public long GetAndIncrement()
        {
            return GetAndAdd(1);
        }

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The new value.</returns>
        public long IncrementAndGet()
        {
            return AddAndGet(1);
        }

        /// <summary>
        /// Gets the current value of the counter and adds an amount to it.
        /// </summary>
        /// <remarks>This uses a CAS loop as Interlocked.Increment is not atomic for longs on 32bit systems.</remarks>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The original value.</returns>
        public long GetAndAdd(long amount)
        {
            while (true)
            {
                var currentValue = _value;
                if (Interlocked.CompareExchange(ref _value, currentValue + amount, currentValue) == currentValue)
                {
                    return currentValue;
                }
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// Adds an amount to the counter and returns the new value.
        /// </summary>
        /// <remarks>This uses a CAS loop as Interlocked.Increment is not atomic for longs on 32bit systems.</remarks>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The new counter value.</returns>
        public long AddAndGet(long amount)
        {
            while (true)
            {
                var currentValue = _value;
                if (Interlocked.CompareExchange(ref _value, currentValue + amount, currentValue) == currentValue)
                {
                    return currentValue + amount;
                }
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// Resets the counter to zero.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _value, 0);
        }
    }
}
