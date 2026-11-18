//-----------------------------------------------------------------------
// <copyright file="AtomicCounterLong.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

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
        /// Retrieves the current value of the counter
        /// </summary>
        public long Current { get { return Interlocked.Read(ref _value); } }

        /// <summary>
        /// Increments the counter and returns the next value.
        /// </summary>
        /// <returns>The incremented counter value.</returns>
        public long Next()
        {
            return Interlocked.Increment(ref _value);
        }

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The original value.</returns>
        public long GetAndIncrement()
        {
            var nextValue = Next();
            return nextValue - 1;
        }

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The new value.</returns>
        public long IncrementAndGet()
        {
            var nextValue = Next();
            return nextValue;
        }

        /// <summary>
        /// Atomically decrements the counter by one
        /// </summary>
        /// <returns>The new value</returns>
        public long DecrementAndGet()
        {
            return Interlocked.Decrement(ref _value);
        }

        /// <summary>
        /// Gets the current value of the counter and adds an amount to it.
        /// </summary>
        /// <remarks>This uses a CAS loop as Interlocked.Increment is not atomic for longs on 32bit systems.</remarks>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The original value.</returns>
        public long GetAndAdd(long amount)
        {
            var newValue = Interlocked.Add(ref _value, amount);
            return newValue - amount;
        }

        /// <summary>
        /// Adds an amount to the counter and returns the new value.
        /// </summary>
        /// <remarks>This uses a CAS loop as Interlocked.Increment is not atomic for longs on 32bit systems.</remarks>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The new counter value.</returns>
        public long AddAndGet(long amount)
        {
            var newValue = Interlocked.Add(ref _value, amount);
            return newValue;
        }

        /// <summary>
        /// Resets the counter to zero.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _value, 0);
        }

        /// <summary>
        /// Returns current counter value and sets a new value on it's place in one operation.
        /// </summary>
        /// <param name="value">The value to store in the counter.</param>
        /// <returns>The counter value before it was replaced.</returns>
        public long GetAndSet(long value)
        {
            return Interlocked.Exchange(ref _value, value);
        }

        /// <summary>
        /// Attempts to replace <paramref name="expected"/> with <paramref name="newValue"/>.
        /// The returned comparison uses the value read from the counter after the atomic exchange, so it does not reliably indicate whether the expected value matched.
        /// </summary>
        /// <param name="expected">The value that the atomic exchange compares against.</param>
        /// <param name="newValue">The value to store if the comparison matches.</param>
        /// <returns><c>true</c> when the value returned by the atomic exchange differs from the subsequent read of the counter; otherwise, <c>false</c>.</returns>
        public bool CompareAndSet(long expected, long newValue)
        {
             return Interlocked.CompareExchange(ref _value, newValue, expected) != _value;
        }

        /// <summary>
        /// Returns the current counter value as a string.
        /// </summary>
        /// <returns>The current value formatted as a string.</returns>
        public override string ToString()
        {
            return Current.ToString();
        }
    }
}
