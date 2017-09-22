//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public static class TaskExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TTask">TBD</typeparam>
        /// <typeparam name="TResult">TBD</typeparam>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public static Task<TResult> CastTask<TTask, TResult>(this Task<TTask> task)
        {
            if (task.IsCompleted)
                return Task.FromResult((TResult) (object)task.Result);
            var tcs = new TaskCompletionSource<TResult>();
            if (task.IsFaulted)
                tcs.SetException(task.Exception);
            else
                task.ContinueWith(_ =>
                {
                    if (task.IsFaulted || task.Exception != null)
                        tcs.SetException(task.Exception);
                    else if (task.IsCanceled)
                        tcs.SetCanceled();
                    else
                        try
                        {
                            tcs.SetResult((TResult) (object) task.Result);
                        }
                        catch (Exception e)
                        {
                            tcs.SetException(e);
                        }
                }, TaskContinuationOptions.ExecuteSynchronously);
            return tcs.Task;
        }

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
    }
}
