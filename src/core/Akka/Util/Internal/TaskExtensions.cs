//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Annotations;

namespace Akka.Util.Internal
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Extensions for working with <see cref="Task"/> types
    /// </summary>
    [InternalApi]
    internal static class TaskExtensions
    {
        /// <summary>
        /// Returns the task which completes with result of original task if cancellation token not canceled it before completion.
        /// </summary>
        /// <param name="task">The original task.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task which completes with result of original task or with cancelled state.</returns>
        public static Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            if (task.IsCompleted || !cancellationToken.CanBeCanceled)
                return task;

            var tcs = new TaskCompletionSource<object>();
            var r = cancellationToken.Register(() => { tcs.SetCanceled(); }, false);

            return Task.WhenAny(task, tcs.Task)
                // Dispose subscription to cancellation token
                .ContinueWith(_ => { r.Dispose(); }, TaskContinuationOptions.ExecuteSynchronously)
                // Check cancellation, to return task in cancelled state instead of completed
                .ContinueWith(_ => { }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

#if NETSTANDARD2_1
        /// <summary>
        /// Polyfill for <c>Task.WaitAsync(TimeSpan)</c> on netstandard2.1.
        /// Returns a task that completes when the original task completes, or faults with
        /// <see cref="TimeoutException"/> if the timeout elapses first.
        /// </summary>
        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(timeout, cts.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
                throw new TimeoutException();
            cts.Cancel();
            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Polyfill for <c>Task.WaitAsync(TimeSpan, CancellationToken)</c> on netstandard2.1.
        /// Returns a task that completes when the original task completes, or faults with
        /// <see cref="TimeoutException"/> if the timeout elapses first, or is canceled via
        /// <paramref name="cancellationToken"/>.
        /// </summary>
        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, linked.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
            linked.Cancel();
            await task.ConfigureAwait(false);
        }

        /// <inheritdoc cref="WaitAsync(Task, TimeSpan)"/>
        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(timeout, cts.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
                throw new TimeoutException();
            cts.Cancel();
            return await task.ConfigureAwait(false);
        }

        /// <inheritdoc cref="WaitAsync(Task, TimeSpan, CancellationToken)"/>
        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, linked.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
            linked.Cancel();
            return await task.ConfigureAwait(false);
        }
#endif

        /// <summary>
        /// When this Task is completed, either through an exception or a value, invoke the provided function.
        /// If the Task has already been completed, this will either be applied immediately or be scheduled asynchronously.
        /// </summary>
        /// <param name="source">TBD</param>
        /// <param name="f">The function to be executed when this Task completes</param>
        public static Task OnComplete(this Task source, Action<Try<Done>> f)
        {
            return source.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var exception = t.Exception?.InnerExceptions != null && t.Exception.InnerExceptions.Count == 1
                        ? t.Exception.InnerExceptions[0]
                        : t.Exception;

                    f(new Try<Done>(exception));
                }
                else
                {
                    f(new Try<Done>(Done.Instance));
                }
            }, TaskContinuationOptions.NotOnCanceled);
        }

        /// <summary>
        /// When this Task is completed, either through an exception or a value, invoke the provided function.
        /// If the Task has already been completed, this will either be applied immediately or be scheduled asynchronously.
        /// </summary>
        /// <param name="source">TBD</param>
        /// <param name="f">The function to be executed when this Task completes</param>
        public static Task OnComplete<TSource>(this Task<TSource> source, Action<Try<TSource>> f)
        {
            return source.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var exception = t.Exception?.InnerExceptions != null && t.Exception.InnerExceptions.Count == 1
                        ? t.Exception.InnerExceptions[0]
                        : t.Exception;

                    f(new Try<TSource>(exception));
                }
                else
                {
                    f(new Try<TSource>(t.Result));
                }
            }, TaskContinuationOptions.NotOnCanceled);
        }
    }
}
