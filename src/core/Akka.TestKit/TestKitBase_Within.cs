using System;
using System.Threading;
using Akka.TestKit.Internals;

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

            var diff = Now - start;
            _assertions.AssertTrue(min <= diff, "block took {0}, should have at least been {1}. {2}", diff, min, hint ?? "");
            if(!_lastWasNoMsg)
            {
                _assertions.AssertTrue(diff <= maxDiff, "block took {0}, exceeding {1}. {2}", diff, maxDiff, hint ?? "");
            }

            return ret;
        }

    }
}