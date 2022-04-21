//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                .ContinueWith(t => { r.Dispose(); }, TaskContinuationOptions.ExecuteSynchronously)
                // Check cancellation, to return task in cancelled state instead of completed
                .ContinueWith(t => { }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

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
