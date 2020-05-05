//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    }
}
