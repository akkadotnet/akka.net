using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Akka.Remote.Artery.Utils
{
    /// <summary>
    /// Implementation of the java.concurrent.util.AtomicLong type.
    /// </summary>
    internal class AtomicLong
    {
        private long _value;

        /// <summary>
        /// Sets the initial value of this <see cref="AtomicLong"/> to <paramref name="initialValue"/>.
        /// </summary>
        /// <param name="initialValue">TBD</param>
        public AtomicLong(long initialValue = 0)
        {
            _value = initialValue;
        }

        /// <summary>
        /// The current value of this <see cref="AtomicLong"/>
        /// </summary>
        public long Value
        {
            get => Interlocked.Read(ref _value);
            set => Interlocked.Exchange(ref _value, value);
        }

        /// <summary>
        /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
        /// <paramref name="newValue"/>.
        /// </summary>
        /// <param name="expected">TBD</param>
        /// <param name="newValue">TBD</param>
        /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
        public bool CompareAndSet(long expected, long newValue)
        {
            var previous = Interlocked.CompareExchange(ref _value, newValue, expected);
            return previous == expected;
        }

        /// <summary>
        /// Atomically sets the <see cref="Value"/> to <paramref name="newValue"/> and returns the old <see cref="Value"/>.
        /// </summary>
        /// <param name="newValue">The new value</param>
        /// <returns>The old value</returns>
        public long GetAndSet(long newValue)
        {
            return Interlocked.Exchange(ref _value, newValue);
        }

        #region Conversion operators

        /// <summary>
        /// Performs an implicit conversion from <see cref="AtomicLong"/> to <see cref="long"/>.
        /// </summary>
        /// <param name="atomicLong">The boolean to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator long(AtomicLong atomicLong)
        {
            return atomicLong.Value;
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="long"/> to <see cref="AtomicLong"/>.
        /// </summary>
        /// <param name="value">The boolean to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator AtomicLong(long value)
        {
            return new AtomicLong(value);
        }

        #endregion

    }
}
