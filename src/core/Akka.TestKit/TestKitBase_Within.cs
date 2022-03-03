//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Within.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit.Internal;
using Nito.AsyncEx.Synchronous;

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
        /// <param name="cancellationToken"></param>
        public void Within(
            TimeSpan max,
            Action action,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            WithinAsync(max, async () => action, epsilonValue, cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of Within
        /// </summary>
        public async Task WithinAsync(
            TimeSpan max,
            Func<Task> actionAsync,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            await WithinAsync(
                min: TimeSpan.Zero,
                max: max,
                actionAsync: actionAsync,
                epsilonValue: epsilonValue,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <param name="cancellationToken"></param>
        public void Within(
            TimeSpan min,
            TimeSpan max,
            Action action,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            WithinAsync(min, max, async () => action, hint, epsilonValue, cancellationToken)
                .WaitAndUnwrapException();
        }
        
        /// <summary>
        /// Async version of <see cref="Within(System.TimeSpan,System.Action,System.Nullable{System.TimeSpan})"/>
        /// </summary>
        public async Task WithinAsync(
            TimeSpan min,
            TimeSpan max,
            Func<Task> actionAsync,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            await WithinAsync<object>(
                min: min,
                max: max,
                function: async () =>
                {
                    await actionAsync();
                    return null;
                }, 
                hint: hint,
                epsilonValue: epsilonValue, 
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T Within<T>(
            TimeSpan max,
            Func<T> function,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            return WithinAsync(
                    min: TimeSpan.Zero,
                    max: max,
                    function: () => Task.FromResult(function()),
                    epsilonValue: epsilonValue, 
                    cancellationToken: cancellationToken)
                .WaitAndUnwrapException();
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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public async Task<T> WithinAsync<T>(
            TimeSpan max,
            Func<Task<T>> function,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            return await WithinAsync(
                    min: TimeSpan.Zero,
                    max: max,
                    function: function, 
                    epsilonValue: epsilonValue,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public T Within<T>(
            TimeSpan min,
            TimeSpan max,
            Func<T> function,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            return WithinAsync(
                    min: min,
                    max: max,
                    function: () => Task.FromResult(function()),
                    hint: hint,
                    epsilonValue: epsilonValue,
                    cancellationToken: cancellationToken)
                .WaitAndUnwrapException();
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
        /// <param name="cancellationToken"></param>
        /// <returns>TBD</returns>
        public async Task<T> WithinAsync<T>(
            TimeSpan min,
            TimeSpan max,
            Func<Task<T>> function,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
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
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using (cts)
                {
                    var funcTask = function();
                    var timeoutTask = Task.Delay(maxDiff + TimeSpan.FromSeconds(1), cts.Token);

                    var finishedTask = await Task.WhenAny(funcTask, timeoutTask);
                    if (finishedTask == funcTask)
                    {
                        ret = await function();
                        cts.Cancel();
                    }
                    else
                    {
                        ret = default;
                    }
                }
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
                epsilonValue ??= TimeSpan.Zero;
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
