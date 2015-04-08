//-----------------------------------------------------------------------
// <copyright file="AtomicCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// Class used for atomic counters and increments.
    /// </summary>
    public class AtomicCounter
    {
        /// <summary>
        /// Creates a new instance initialized to the value specified by <paramref name="initialValue"/>.
        /// If  <paramref name="initialValue"/> is not specified it defaults to -1.
        /// </summary>
        /// <param name="initialValue"></param>
        public AtomicCounter(int initialValue=-1)
        {
            _value = initialValue;
        }

        private int _value;

        /// <summary>
        /// Retrieves the current value of the counter
        /// </summary>
        public int Current { get { return _value; } }

        /// <summary>
        /// Increments the counter and returns the next value
        /// </summary>
        public int Next
        {
            get
            {
                return Interlocked.Increment(ref _value);
            }
        }

        /// <summary>
        /// Returns the current value while simultaneously incrementing the counter
        /// </summary>
        public int GetAndIncrement()
        {
            var nextValue = Next;
            return nextValue-1;
        }

        /// <summary>
        /// Increments the counter and returns the new value
        /// </summary>
        public int IncrementAndGet()
        {
            var nextValue = Next;
            return nextValue;
        }

        /// <summary>
        /// Returns the current value and adds the specified value to the counter.
        /// </summary>
        /// <param name="amount"></param>
        /// <returns></returns>
        public int GetAndAdd(int amount)
        {
            var newValue=Interlocked.Add(ref _value, amount);
            return newValue-amount;
        }


        /// <summary>
        /// Adds the specified value to the counter and returns the new value
        /// </summary>
        /// <param name="amount"></param>
        /// <returns></returns>
        public int AddAndGet(int amount)
        {
            var newValue = Interlocked.Add(ref _value, amount);
            return newValue;
        }
    }
}
