//-----------------------------------------------------------------------
// <copyright file="FutureTimeoutSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Pattern
{
    /// <summary>
    /// Used to help make it easier to schedule timeouts in conjunction
    /// with the built-in <see cref="IScheduler"/>
    /// </summary>
    public static class FutureTimeoutSupport
    {

        /// <summary>
        /// Returns a <see cref="Task"/> that will be completed with the success or failure
        /// of the provided value after the specified duration.
        /// </summary>
        /// <typeparam name="T">The return type of task.</typeparam>
        /// <param name="duration">The duration to wait.</param>
        /// <param name="scheduler">The scheduler instance to use.</param>
        /// <param name="value">The task we're going to wrap.</param>
        /// <returns>a <see cref="Task"/> that will be completed with the success or failure
        /// of the provided value after the specified duration</returns>
        public static Task<T> After<T>(TimeSpan duration, IScheduler scheduler, Func<Task<T>> value)
        {
            if (duration < TimeSpan.MaxValue && duration.Ticks < 1)
            {
                // no need to schedule
                try
                {
                    return value();
                }
                catch (Exception ex)
                {
                    return TaskEx.FromException<T>(ex);
                }
            }

            var tcs = new TaskCompletionSource<T>();
            scheduler.Advanced.ScheduleOnce(duration, () =>
            {
                try
                {
                    value().ContinueWith(tr =>
                    {
                        try
                        {
                            if (tr.IsCanceled || tr.IsFaulted)
                            {
                                tcs.SetException(tr.Exception.InnerException);
                            }
                            else
                                tcs.SetResult(tr.Result);
                        }
                        catch (AggregateException ex)
                        {
                            // in case the task faults
                            tcs.SetException(ex.Flatten());
                        }
                    });
                }
                catch (Exception ex)
                { 
                    // in case the value() function faults
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
