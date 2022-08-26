﻿//-----------------------------------------------------------------------
// <copyright file="TestKitBase_Within.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit.Internal;
using FluentAssertions.Extensions;
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
            WithinAsync(
                    min: TimeSpan.Zero,
                    max: max,
                    function: async () =>
                    {
                        action();
                        return NotUsed.Instance;
                    },
                    hint: null,
                    epsilonValue: epsilonValue,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Async version of <see cref="Within(TimeSpan, Action, TimeSpan?, CancellationToken)"/>
        /// that takes a <see cref="Func{Task}"/> instead of an <see cref="Action"/>
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
                function: async () =>
                {
                    await actionAsync().ConfigureAwait(false);
                    return NotUsed.Instance;
                },
                hint: null,
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
            WithinAsync(
                    min: min, 
                    max: max, 
                    function: async () =>
                    {
                        action();
                        return NotUsed.Instance;
                    }, 
                    hint: hint, 
                    epsilonValue: epsilonValue, 
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Async version of <see cref="Within(TimeSpan, TimeSpan, Action, string, TimeSpan?, CancellationToken)"/>
        /// that takes a <see cref="Func{Task}"/> instead of an <see cref="Action"/>
        /// </summary>
        public async Task WithinAsync(
            TimeSpan min,
            TimeSpan max,
            Func<Task> actionAsync,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
        {
            await WithinAsync(
                min: min,
                max: max,
                function: async () =>
                {
                    await actionAsync().ConfigureAwait(false);
                    return (object)null;
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
                    function: async () => function(),
                    hint: null,
                    epsilonValue: epsilonValue,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
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
                    hint: null,
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
                    function: async () => function(),
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
        /// <remarks>
        /// <para>
        /// Note that the max duration is scaled using <see cref="Dilated(TimeSpan)"/> which uses the config value "akka.test.timefactor".
        /// </para>
        /// <para>
        /// Note that due to how asynchronous Task is executed in managed code, there is no way to stop a running Task.
        /// If this assertion fails in any way, the <paramref name="function"/> Task might still be running in the
        /// background and might not be stopped/disposed until the unit test is over.
        /// </para>
        /// </remarks>
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
            max.EnsureIsPositiveFinite("max");
            max = Dilated(max);
            var start = Now;
            var rem = _testState.End.HasValue ? _testState.End.Value - start : Timeout.InfiniteTimeSpan;
            _assertions.AssertTrue(rem.IsInfiniteTimeout() || rem >= min, "Required min time {0} not possible, only {1} left. {2}", min, rem, hint ?? "");

            _testState.LastWasNoMsg = false;

            var maxDiff = max.Min(rem);
            var prevEnd = _testState.End;
            _testState.End = start + maxDiff;

            T ret = default;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    var executionTask = function();
                    // Limit the execution time block to the maximum allowed execution time.
                    // 200 milliseconds is added because Task.Delay() timer is not precise and can return prematurely.
                    var resultTask = await Task.WhenAny(executionTask, Task.Delay(max + 200.Milliseconds(), cts.Token));

                    if (resultTask == executionTask)
                    {
                        ret = executionTask.Result;
                    }
                    else
                    {
                        // Just throw if the calling code cancels the cancellation token
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                finally
                {
                    // Make sure we stop the delay task
                    cts.Cancel();
                    _testState.End = prevEnd;
                }
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
