//-----------------------------------------------------------------------
// <copyright file="AtomicReference.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class AtomicReference<T>
        where T : class
    {
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicReference{T}"/> to <paramref name="originalValue"/>.
        /// </summary>
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
        /// Implicit conversion operator = automatically casts the <see cref="AtomicReference{T}"/> to an instance of <typeparamref name="T"/>.
        /// </summary>
        public static implicit operator T(AtomicReference<T> aRef)
        {
            return aRef.Value;
        }

        /// <summary>
        /// Implicit conversion operator = allows us to cast any type directly into a <see cref="AtomicReference{T}"/> instance.
        /// </summary>
        /// <param name="newValue"></param>
        /// <returns></returns>
        public static implicit operator AtomicReference<T>(T newValue)
        {
            return new AtomicReference<T>(newValue);
        }

        #endregion
    }
}

