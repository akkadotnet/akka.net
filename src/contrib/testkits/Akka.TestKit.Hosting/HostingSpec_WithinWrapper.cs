// -----------------------------------------------------------------------
//  <copyright file="HostingSpec_WithinWrapper.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.TestKit.Hosting
{
    public abstract partial class HostingSpec
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
            => TestKit.Within(max, action, epsilonValue, cancellationToken);
        
        /// <summary>
        /// Async version of <see cref="Within(TimeSpan, Action, TimeSpan?, CancellationToken)"/>
        /// that takes a <see cref="Func{Task}"/> instead of an <see cref="Action"/>
        /// </summary>
        public Task WithinAsync(
            TimeSpan max,
            Func<Task> actionAsync,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
            => TestKit.WithinAsync(max, actionAsync, epsilonValue, cancellationToken);

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
            => TestKit.Within(min, max, action, hint, epsilonValue, cancellationToken);
        
        /// <summary>
        /// Async version of <see cref="Within(TimeSpan, TimeSpan, Action, string, TimeSpan?, CancellationToken)"/>
        /// that takes a <see cref="Func{Task}"/> instead of an <see cref="Action"/>
        /// </summary>
        public Task WithinAsync(
            TimeSpan min,
            TimeSpan max,
            Func<Task> actionAsync,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
            => TestKit.WithinAsync(min, max, actionAsync, hint, epsilonValue, cancellationToken);

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
            => TestKit.Within(max, function, epsilonValue, cancellationToken);

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
        public Task<T> WithinAsync<T>(
            TimeSpan max,
            Func<Task<T>> function,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
            => TestKit.WithinAsync(max, function, epsilonValue, cancellationToken);

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
            => TestKit.Within(min, max, function, hint, epsilonValue, cancellationToken);

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
        public Task<T> WithinAsync<T>(
            TimeSpan min,
            TimeSpan max,
            Func<Task<T>> function,
            string hint = null,
            TimeSpan? epsilonValue = null,
            CancellationToken cancellationToken = default)
            => TestKit.WithinAsync(min, max, function, hint, epsilonValue, cancellationToken);
    }
}