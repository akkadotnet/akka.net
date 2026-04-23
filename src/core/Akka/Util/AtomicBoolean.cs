//-----------------------------------------------------------------------
// <copyright file="AtomicBoolean.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

#nullable enable
namespace Akka.Util
{
    /// <summary>
    /// Implementation of the java.concurrent.util.AtomicBoolean type.
    /// 
    /// Uses <see cref="Interlocked.MemoryBarrier"/> internally to enforce ordering of writes
    /// without any explicit locking. .NET's strong memory on write guarantees might already enforce
    /// this ordering, but the addition of the MemoryBarrier guarantees it.
    /// </summary>
    public class AtomicBoolean
    {
        private const int FalseValue = 0;
        private const int TrueValue = 1;

        private int _value;
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicBoolean"/> to <paramref name="initialValue"/>.
        /// </summary>
        /// <param name="initialValue">TBD</param>
        public AtomicBoolean(bool initialValue = false)
        {
            _value = initialValue ? TrueValue : FalseValue;
        }

        /// <summary>
        /// The current value of this <see cref="AtomicReference{T}"/>
        /// </summary>
        public bool Value
        {
            get
            {
                Interlocked.MemoryBarrier();
                return _value==TrueValue;
            }
            set
            {
                Interlocked.Exchange(ref _value, value ? TrueValue : FalseValue);
            }
        }

        /// <summary>
        /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
        /// <paramref name="newValue"/>.
        /// </summary>
        /// <param name="expected">TBD</param>
        /// <param name="newValue">TBD</param>
        /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
        public bool CompareAndSet(bool expected, bool newValue)
        {
            var expectedInt = expected ? TrueValue : FalseValue;
            var newInt = newValue ? TrueValue : FalseValue;
            return Interlocked.CompareExchange(ref _value, newInt, expectedInt) == expectedInt;
        }

        /// <summary>
        /// Atomically sets the <see cref="Value"/> to <paramref name="newValue"/> and returns the old <see cref="Value"/>.
        /// </summary>
        /// <param name="newValue">The new value</param>
        /// <returns>The old value</returns>
        public bool GetAndSet(bool newValue)
        {
            return Interlocked.Exchange(ref _value, newValue ? TrueValue : FalseValue) == TrueValue;
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="AtomicBoolean"/> to <see cref="System.Boolean"/>.
        /// </summary>
        /// <param name="atomicBoolean">The boolean to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator bool(AtomicBoolean atomicBoolean)
        {
            return atomicBoolean.Value;
        }
    }
}

