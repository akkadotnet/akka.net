using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Akka.Remote.Artery.Utils
{
    internal class AtomicInt
    {
        private int _value;
        private readonly object _lock = new object();

        /// <summary>
        /// Sets the initial value of this <see cref="AtomicLong"/> to <paramref name="initialValue"/>.
        /// </summary>
        /// <param name="initialValue">TBD</param>
        public AtomicInt(int initialValue = 0)
        {
            _value = initialValue;
        }

        /// <summary>
        /// The current value of this <see cref="AtomicInt"/>
        /// </summary>
        public int Value
        {
            get {
                lock (_lock)
                {
                    return _value;
                }
            }
            set => Interlocked.Exchange(ref _value, value);
        }

        /// <summary>
        /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
        /// <paramref name="newValue"/>.
        /// </summary>
        /// <param name="expected">TBD</param>
        /// <param name="newValue">TBD</param>
        /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
        public bool CompareAndSet(int expected, int newValue)
        {
            var previous = Interlocked.CompareExchange(ref _value, newValue, expected);
            return previous == expected;
        }

        /// <summary>
        /// Atomically sets the <see cref="Value"/> to <paramref name="newValue"/> and returns the old <see cref="Value"/>.
        /// </summary>
        /// <param name="newValue">The new value</param>
        /// <returns>The old value</returns>
        public int GetAndSet(int newValue)
            => Interlocked.Exchange(ref _value, newValue);

        public int GetAndAdd(int delta)
        {
            lock (_lock)
            {
                var pref = _value;
                _value += delta;
                return pref;
            }
        }

        public int GetAndIncrement()
        {
            lock (_lock)
            {
                var pref = _value;
                ++_value;
                return pref;
            }
        }

        public int Increment()
            => Interlocked.Increment(ref _value);

        public int GetAndDecrement()
        {
            lock (_lock)
            {
                var pref = _value;
                --_value;
                return pref;
            }
        }

        public int Decrement()
            => Interlocked.Decrement(ref _value);
    }
}
