//-----------------------------------------------------------------------
// <copyright file="TimeSpanExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// This class contains extension methods used to simplify working with <see cref="TimeSpan">TimeSpans</see>.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class TimeSpanExtensions
    {
        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> has no value.
        /// </summary>
        /// <param name="timeSpan">The timespan used to check for a value</param>
        /// <returns><c>true</c> if the given timespan has no value; otherwise, <c>false</c>.</returns>
        public static bool IsUndefined(this TimeSpan? timeSpan)
        {
            return !timeSpan.HasValue;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> has zero ticks.
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has zero ticks; otherwise, <c>false</c>.</returns>
        public static bool IsZero(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks == 0;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> has zero ticks.
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has zero ticks; otherwise, <c>false</c>.</returns>
        public static bool IsZero(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks == 0;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> has one or more ticks.
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has one or more ticks; otherwise, <c>false</c>.</returns>
        public static bool IsPositiveFinite(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks >= 0;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> has one or more ticks.
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has one or more ticks; otherwise, <c>false</c>.</returns>
        public static bool IsPositiveFinite(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks >= 0;
        }

        /// <summary>
        /// <para>
        /// Determine if the supplied <paramref name="timeSpan"/> has a negative number of ticks.
        /// </para>
        /// <note>
        /// This is a relaxed definition of a <see cref="TimeSpan"/>.
        /// For a stricter definition, use <see cref="IsInfiniteTimeout(System.TimeSpan)"/> 
        /// </note>
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has a negative number of ticks; otherwise, <c>false</c>.</returns>
        public static bool IsInfinite(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks < 0;
        }

        /// <summary>
        /// <para>
        /// Determine if the supplied <paramref name="timeSpan"/> has a negative number of ticks.
        /// </para>
        /// <note>
        /// This is a relaxed definition of a <see cref="TimeSpan"/>.
        /// For a stricter definition, use <see cref="IsInfiniteTimeout(System.TimeSpan)"/> 
        /// </note>
        /// </summary>
        /// <param name="timeSpan">The timespan used to check the number of ticks.</param>
        /// <returns><c>true</c> if the given timespan has a negative number of ticks; otherwise, <c>false</c>.</returns>
        public static bool IsInfinite(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks < 0;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> is equal to <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <param name="timeSpan">The timespan used for comparison.</param>
        /// <returns><c>true</c> if the given timespan is equal to <see cref="Timeout.InfiniteTimeSpan"/>; otherwise, <c>false</c>.</returns>
        public static bool IsInfiniteTimeout(this TimeSpan timeSpan)
        {
            return timeSpan == Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Determine if the supplied <paramref name="timeSpan"/> is equal to <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <param name="timeSpan">The timespan used for comparison.</param>
        /// <returns><c>true</c> if the given timespan is equal to <see cref="Timeout.InfiniteTimeSpan"/>; otherwise, <c>false</c>.</returns>
        public static bool IsInfiniteTimeout(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value == Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Throws an <see cref="ArgumentException"/> if the <paramref name="timeSpan"/> is not 0 or greater.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        /// <param name="timeSpan">The timespan used for comparison.</param>
        /// <param name="parameterName">The name of the timespan.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the given timespan has zero or less ticks.
        /// </exception>
        public static void EnsureIsPositiveFinite(this TimeSpan timeSpan, string parameterName)
        {
            if(!IsPositiveFinite(timeSpan))
                throw new ArgumentException($"The timespan must be greater than zero. Actual value: {timeSpan}", nameof(parameterName));
        }

        /// <summary>
        /// <para>
        /// Compares two supplied timespans and returns the timespan with the least amount of positive ticks.
        /// </para>
        /// <para>
        /// If <paramref name="b"/> is <c>null</c> it's treated as 
        /// undefined, and <paramref name="a"/> is returned.
        /// </para>
        /// </summary>
        /// <param name="a">The first timespan used for comparison.</param>
        /// <param name="b">The second timespan used for comparison</param>
        /// <returns>The timespan with the least amount of ticks between the two given timespans.</returns>
        public static TimeSpan Min(this TimeSpan a, TimeSpan? b)
        {
            if(!b.HasValue) return a;
            var bValue = b.Value;
            if(bValue.IsInfinite()) return a;
            if(a.IsInfinite())
            {
                return bValue;
            }
            return a < bValue ? a : bValue;
        }
    }
}
