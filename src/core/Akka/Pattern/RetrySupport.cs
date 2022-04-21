//-----------------------------------------------------------------------
// <copyright file="RetrySupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;
using static Akka.Pattern.FutureTimeoutSupport;

namespace Akka.Pattern
{
    /// <summary>
    /// This class provides the retry utility functions.
    /// </summary>
    public static class RetrySupport
    {
        /// <summary>
        /// <para>
        /// Given a function, returns an internally retrying Task.
        /// The first attempt will be made immediately, each subsequent attempt will be made immediately
        /// if the previous attempt failed.
        /// </para>
        /// If attempts are exhausted the returned Task is simply the result of invoking attempt.
        /// </summary>
        /// <param name="attempt">TBD</param>
        /// <param name="attempts">TBD</param>
        public static Task<T> Retry<T>(Func<Task<T>> attempt, int attempts) =>
            Retry(attempt, attempts, attempted: 0);

        /// <summary>
        /// <para>
        /// Given a function, returns an internally retrying Task.
        /// The first attempt will be made immediately, each subsequent attempt will be made with a backoff time,
        /// if the previous attempt failed.
        /// </para>
        /// If attempts are exhausted the returned Task is simply the result of invoking attempt.
        /// </summary>
        /// <param name="attempt">TBD</param>
        /// <param name="attempts">TBD</param>
        /// <param name="minBackoff">minimum (initial) duration until the child actor will started again, if it is terminated.</param>
        /// <param name="maxBackoff">the exponential back-off is capped to this duration.</param>
        /// <param name="randomFactor">after calculation of the exponential back-off an additional random delay based on this factor is added, e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`.</param>
        /// <param name="scheduler">The scheduler instance to use.</param>
        public static Task<T> Retry<T>(Func<Task<T>> attempt, int attempts, TimeSpan minBackoff, TimeSpan maxBackoff, int randomFactor, IScheduler scheduler)
        {
            if (attempt == null) throw new ArgumentNullException("Parameter attempt should not be null.");
            if (minBackoff <= TimeSpan.Zero) throw new ArgumentException("Parameter minBackoff must be > 0");
            if (maxBackoff < minBackoff) throw new ArgumentException("Parameter maxBackoff must be >= minBackoff");
            if (randomFactor < 0.0 || randomFactor > 1.0) throw new ArgumentException("RandomFactor must be between 0.0 and 1.0");

            return Retry(attempt, attempts, attempted => BackoffSupervisor.CalculateDelay(attempted, minBackoff, maxBackoff, randomFactor), scheduler);
        }

        /// <summary>
        /// <para>
        /// Given a function, returns an internally retrying Task.
        /// The first attempt will be made immediately, each subsequent attempt will be made after 'delay'.
        /// A scheduler (eg Context.System.Scheduler) must be provided to delay each retry.
        /// </para>
        /// If attempts are exhausted the returned future is simply the result of invoking attempt.
        /// </summary>
        /// <param name="attempt">TBD</param>
        /// <param name="attempts">TBD</param>
        /// <param name="delay">TBD</param>
        /// <param name="scheduler">The scheduler instance to use.</param>
        public static Task<T> Retry<T>(Func<Task<T>> attempt, int attempts, TimeSpan delay, IScheduler scheduler) =>
            Retry(attempt, attempts, _ => delay, scheduler);

        /// <summary>
        /// <para>
        /// Given a function, returns an internally retrying Task.
        /// The first attempt will be made immediately, each subsequent attempt will be made after
        /// the 'delay' return by `delayFunction`(the input next attempt count start from 1).
        /// Returns <see cref="Option{TimeSpan}.None"/> for no delay.
        /// A scheduler (eg Context.System.Scheduler) must be provided to delay each retry.
        /// You could provide a function to generate the next delay duration after first attempt,
        /// this function should never return `null`, otherwise an <see cref="InvalidOperationException"/> will be through.
        /// </para>
        /// If attempts are exhausted the returned Task is simply the result of invoking attempt.
        /// </summary>
        /// <param name="attempt">TBD</param>
        /// <param name="attempts">TBD</param>
        /// <param name="delayFunction">TBD</param>
        /// <param name="scheduler">The scheduler instance to use.</param>
        public static Task<T> Retry<T>(Func<Task<T>> attempt, int attempts, Func<int, Option<TimeSpan>> delayFunction, IScheduler scheduler) =>
            Retry(attempt, attempts, delayFunction, attempted: 0, scheduler);

        private static Task<T> Retry<T>(Func<Task<T>> attempt, int maxAttempts, int attempted) =>
            Retry(attempt, maxAttempts, _ => Option<TimeSpan>.None, attempted);

        private static Task<T> Retry<T>(Func<Task<T>> attempt, int maxAttempts, Func<int, Option<TimeSpan>> delayFunction, int attempted, IScheduler scheduler = null)
        {
            Task<T> tryAttempt()
            {
                try
                {
                    return attempt();
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex); // in case the `attempt` function throws
                }
            }

            if (maxAttempts < 0) throw new ArgumentException("Parameter maxAttempts must >= 0.");
            if (attempt == null) throw new ArgumentNullException(nameof(attempt), "Parameter attempt should not be null.");

            if (maxAttempts - attempted > 0)
            {
                return tryAttempt().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var nextAttempt = attempted + 1;
                        switch (delayFunction(nextAttempt))
                        {
                            case Option<TimeSpan> delay when delay.HasValue:
                                return delay.Value.Ticks < 1
                                    ? Retry(attempt, maxAttempts, delayFunction, nextAttempt, scheduler)
                                    : After(delay.Value, scheduler, () => Retry(attempt, maxAttempts, delayFunction, nextAttempt, scheduler));
                            case Option<TimeSpan> _:
                                return Retry(attempt, maxAttempts, delayFunction, nextAttempt, scheduler);
                            default:
                                throw new InvalidOperationException("The delayFunction of Retry should not return null.");
                        }
                    }
                    return t;
                }).Unwrap();
            }

            return tryAttempt();
        }
    }
}
