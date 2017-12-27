//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
        /// Casts an asynchronous result of type <typeparamref name="TSource"/> from given task
        /// onto the task with result type of <typeparamref name="TResult"/>. Additionally if
        /// source is an Akka <see cref="Status.Failure"/> message will be unwrapped and delivered error
        /// will be rethrown if that situation occurs.
        /// </summary>
        /// <typeparam name="TSource">Incoming returned type param wrapped within a task.</typeparam>
        /// <typeparam name="TResult">Expected result type param returned asynchronously as a task.</typeparam>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public static async Task<TResult> CastTask<TSource, TResult>(this Task<TSource> task)
        {
            object result = await task;

            // special case for Ask<> method - if a Failure message is returned, rethrow it as failed
            // task (unless an user explicitly said, he expected a Failure message to be returned)
            if (result is Status.Failure failure && typeof(TResult) != typeof(Status.Failure))
            {
                ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                return default(TResult);
            }
            else return (TResult)result;
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
