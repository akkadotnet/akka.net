﻿//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Within.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// Execute code block while bounding its execution time between 0 seconds and <paramref name="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        /// <param name="max">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="epsilonValue">TBD</param>
        public void Within(TimeSpan max, Action action, TimeSpan? epsilonValue = null)
        {
            Within(TimeSpan.Zero, max, action, epsilonValue: epsilonValue);
        }

        /// <summary>
        /// Execute code block while bounding its execution time between <paramref name="min"/> and <paramref name="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        /// <param name="min">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="epsilonValue">TBD</param>
        public void Within(TimeSpan min, TimeSpan max, Action action, string hint = null, TimeSpan? epsilonValue = null)
        {
            Within<object>(min, max, () => { action(); return null; }, hint, epsilonValue);
        }


        /// <summary>
        /// Execute code block while bounding its execution time between 0 seconds and <paramref name="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="max">TBD</param>
        /// <param name="function">TBD</param>
        /// <param name="epsilonValue">TBD</param>
        /// <returns>TBD</returns>
        public T Within<T>(TimeSpan max, Func<T> function, TimeSpan? epsilonValue = null)
        {
            return Within(TimeSpan.Zero, max, function, epsilonValue: epsilonValue);
        }

        /// <summary>
        /// Execute code block while bounding its execution time between <paramref name="min"/> and <paramref name="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="min">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="function">TBD</param>
        /// <param name="hint">TBD</param>
        /// <param name="epsilonValue">TBD</param>
        /// <returns>TBD</returns>
        public T Within<T>(TimeSpan min, TimeSpan max, Func<T> function, string hint = null, TimeSpan? epsilonValue = null)
        {
            min.EnsureIsPositiveFinite("min");
            min.EnsureIsPositiveFinite("max");
            max = Dilated(max);
            var start = Now;
            var rem = _testState.End.HasValue ? _testState.End.Value - start : Timeout.InfiniteTimeSpan;
            _assertions.AssertTrue(rem.IsInfiniteTimeout() || rem >= min, "Required min time {0} not possible, only {1} left. {2}", min, rem, hint ?? "");

            _testState.LastWasNoMsg = false;

            var maxDiff = max.Min(rem);
            var prevEnd = _testState.End;
            _testState.End = start + maxDiff;

            T ret;
            try
            {
                ret = function();
            }
            finally
            {
                _testState.End = prevEnd;
            }

            var elapsed = Now - start;
            var wasTooFast = elapsed < min;
            if(wasTooFast)
            {
                const string failMessage = "Failed: Block took {0}, should have at least been {1}. {2}";
                ConditionalLog(failMessage, elapsed, min, hint ?? "");
                _assertions.Fail(failMessage, elapsed, min, hint ?? "");
            }
            if (!_testState.LastWasNoMsg)
            {
                epsilonValue = epsilonValue ?? TimeSpan.Zero;
                var tookTooLong = elapsed > maxDiff + epsilonValue;
                if(tookTooLong)
                {
                    const string failMessage = "Failed: Block took {0}, exceeding {1}. {2}";
                    ConditionalLog(failMessage, elapsed, maxDiff, hint ?? "");
                    _assertions.Fail(failMessage, elapsed, maxDiff, hint ?? "");
                }
            }

            return ret;
        }

    }
}