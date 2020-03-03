//-----------------------------------------------------------------------
// <copyright file="AtomicReference.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// Implementation of the java.concurrent.util AtomicReference type.
    /// 
    /// Uses <see cref="Volatile"/> internally to enforce ordering of writes
    /// without any explicit locking. .NET's strong memory on write guarantees might already enforce
    /// this ordering, but the addition of the Volatile guarantees it.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class AtomicReference<T>
        where T : class
    {
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicReference{T}"/> to <paramref name="originalValue"/>.
        /// </summary>
        /// <param name="originalValue">TBD</param>
        public AtomicReference(T originalValue)
        {
            atomicValue = originalValue;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public AtomicReference()
        {
            atomicValue = default(T);
        }

        // ReSharper disable once InconsistentNaming
        /// <summary>
        /// TBD
        /// </summary>
        protected T atomicValue;

        /// <summary>
        /// The current value of this <see cref="AtomicReference{T}"/>
        /// </summary>
        public T Value
        {
            get { return Volatile.Read(ref atomicValue); }
            set { Volatile.Write(ref atomicValue, value); }
        }

        /// <summary>
        /// If <see cref="Value"/> equals <paramref name="expected"/>, then set the Value to
        /// <paramref name="newValue"/>.
        /// </summary>
        /// <param name="expected">TBD</param>
        /// <param name="newValue">TBD</param>
        /// <returns><c>true</c> if <paramref name="newValue"/> was set</returns>
        public bool CompareAndSet(T expected, T newValue)
        {
            var previous = Interlocked.CompareExchange(ref atomicValue, newValue, expected);
            return ReferenceEquals(previous, expected);
        }

        /// <summary>
        /// Atomically sets the <see cref="Value"/> to <paramref name="newValue"/> and returns the old <see cref="Value"/>.
        /// </summary>
        /// <param name="newValue">The new value</param>
        /// <returns>The old value</returns>
        public T GetAndSet(T newValue)
        {
            return Interlocked.Exchange(ref atomicValue, newValue);
        }

        #region Conversion operators

        /// <summary>
        /// Performs an implicit conversion from <see cref="AtomicReference{T}"/> to <typeparamref name="T"/>.
        /// </summary>
        /// <param name="atomicReference">The reference to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator T(AtomicReference<T> atomicReference)
        {
            return atomicReference.Value;
        }

        /// <summary>
        /// Performs an implicit conversion from <typeparamref name="T"/> to <see cref="AtomicReference{T}"/>.
        /// </summary>
        /// <param name="value">The reference to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator AtomicReference<T>(T value)
        {
            return new AtomicReference<T>(value);
        }

        #endregion
    }
}

