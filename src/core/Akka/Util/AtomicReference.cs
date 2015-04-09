//-----------------------------------------------------------------------
// <copyright file="AtomicReference.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

namespace Akka.Util
{
    /// <summary>
    /// Implementation of the java.concurrent.util AtomicReference type.
    /// 
    /// Uses <see cref="Interlocked.MemoryBarrier"/> internally to enforce ordering of writes
    /// without any explicit locking. .NET's strong memory on write guarantees might already enforce
    /// this ordering, but the addition of the MemoryBarrier guarantees it.
    /// </summary>
    public class AtomicReference<T>
    {
        /// <summary>
        /// Sets the initial value of this <see cref="AtomicReference{T}"/> to <see cref="originalValue"/>.
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
            get
            {
                Interlocked.MemoryBarrier();
                return atomicValue;
            }
            set
            {
                Interlocked.MemoryBarrier();
                atomicValue = value;
                Interlocked.MemoryBarrier();
            }
        }

        /// <summary>
        /// If <see cref="Value"/> equals <see cref="expected"/>, then set the Value to
        /// <see cref="newValue"/>.
        /// 
        /// Returns true if <see cref="newValue"/> was set, false otherise.
        /// </summary>
        public bool CompareAndSet(T expected, T newValue)
        {
            //special handling for null values
            if (Value == null)
            {
                if (expected == null)
                {
                    Value = newValue;
                    return true;
                }
                return false;
            }

            if (Value.Equals(expected))
            {
                Value = newValue;
                return true;
            }
            return false;
        }

        #region Conversion operators

        /// <summary>
        /// Implicit conversion operator = automatically casts the <see cref="AtomicReference{T}"/> to an instance of <typeparam name="T"></typeparam>
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

