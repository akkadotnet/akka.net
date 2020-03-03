//-----------------------------------------------------------------------
// <copyright file="IAtomicCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Util.Internal
{
    /// <summary>
    /// An interface that describes a numeric counter.
    /// </summary>
    /// <typeparam name="T">The type of the numeric.</typeparam>
    public interface IAtomicCounter<T>
    {
        /// <summary>
        /// The current value of this counter.
        /// </summary>
        T Current { get; }

        /// <summary>
        /// Increments the counter and gets the next value. This is exactly the same as calling <see cref="IncrementAndGet"/>.
        /// </summary>
        T Next();

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The original value.</returns>
        T GetAndIncrement();

        /// <summary>
        /// Atomically increments the counter by one.
        /// </summary>
        /// <returns>The new value.</returns>
        T IncrementAndGet();

        /// <summary>
        /// Returns the current value and adds the specified value to the counter.
        /// </summary>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The original value before additions.</returns>
        T GetAndAdd(T amount);

        /// <summary>
        /// Adds the specified value to the counter and returns the new value.
        /// </summary>
        /// <param name="amount">The amount to add to the counter.</param>
        /// <returns>The new value after additions.</returns>
        T AddAndGet(T amount);

        /// <summary>
        /// Resets the counter to zero.
        /// </summary>
        void Reset();
    }
}

