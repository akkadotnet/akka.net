//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Pattern
{
    public static class FutureTimeoutSupport
    {
        /// <summary>
        /// Returns a Task that will be completed with the success or failure of the provided value after the specified duration.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="scheduler">TBD</param>
        public static Task<T> After<T>(TimeSpan timeout, IScheduler scheduler, Func<Task<T>> value)
        {
            var promise = new TaskCompletionSource<T>();

            scheduler.Advanced.ScheduleOnce(timeout, () =>
            {
                value().ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled)
                        promise.SetCanceled();
                    else
                        promise.SetResult(t.Result);
                });
            });
            return promise.Task;
        }
    }
}
