using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Tools
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
    }
}
