//-----------------------------------------------------------------------
// <copyright file="AtomicCounterLong.cs" company="Akka.NET Project">
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

namespace Akka.Util
{
    /// <summary>
    /// Atomic counter that uses longs internally
    /// </summary>
    public class AtomicCounterLong
    {
        public AtomicCounterLong(long seed)
        {
            _seed = seed;
        }

        private long _seed;

        /// <summary>
        /// Retrieves the current value of the counter
        /// </summary>
        public long Current { get { return _seed; } }

        /// <summary>
        /// Increments the counter and returns the next value
        /// </summary>
        public long Next
        {
            get
            {
                return Interlocked.Increment(ref _seed);
            }
        }

        /// <summary>
        /// Returns the current value while simultaneously incrementing the counter
        /// </summary>
        public long GetAndIncrement()
        {
            var rValue = Current;
            var nextValue = Next;
            return rValue;
        }
    }
}

