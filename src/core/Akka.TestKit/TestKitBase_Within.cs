//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Within.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
    public abstract partial class TestKitBase
    {

        /// <summary>
        /// Execute code block while bounding its execution time between 0 seconds and <see cref="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        public void Within(TimeSpan max, Action action)
        {
            Within(TimeSpan.Zero, max, action);
        }

        /// <summary>
        /// Execute code block while bounding its execution time between <see cref="min"/> and <see cref="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        public void Within(TimeSpan min, TimeSpan max, Action action, string hint = null)
        {
            Within<object>(min, max, () => { action(); return null; }, hint);
        }


        /// <summary>
        /// Execute code block while bounding its execution time between 0 seconds and <see cref="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        public T Within<T>(TimeSpan max, Func<T> function)
        {
            return Within(TimeSpan.Zero, max, function);
        }

        /// <summary>
        /// Execute code block while bounding its execution time between <see cref="min"/> and <see cref="max"/>.
        /// <para>`within` blocks may be nested. All methods in this class which take maximum wait times 
        /// are available in a version which implicitly uses the remaining time governed by 
        /// the innermost enclosing `within` block.</para>
        /// <remarks>Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor"</remarks>
        /// </summary>
        public T Within<T>(TimeSpan min, TimeSpan max, Func<T> function, string hint = null)
        {
            min.EnsureIsPositiveFinite("min");
            min.EnsureIsPositiveFinite("max");
            max = Dilated(max);
            var start = Now;
            var rem = _end.HasValue ? _end.Value - start : Timeout.InfiniteTimeSpan;
            _assertions.AssertTrue(rem.IsInfiniteTimeout() || rem >= min, "Required min time {0} not possible, only {1} left. {2}", min, rem, hint ?? "");

            _lastWasNoMsg = false;

            var maxDiff = max.Min(rem);
            var prevEnd = _end;
            _end = start + maxDiff;

            T ret;
            try
            {
                ret = function();
            }
            finally
            {
                _end = prevEnd;
            }

            var elapsed = Now - start;
            var wasTooFast = elapsed < min;
            if(wasTooFast)
            {
                const string failMessage = "Failed: Block took {0}, should have at least been {1}. {2}";
                ConditionalLog(failMessage, elapsed, min, hint ?? "");
                _assertions.Fail(failMessage, elapsed, min, hint ?? "");
            }
            if(!_lastWasNoMsg)
            {
                var tookTooLong = elapsed > maxDiff;
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

