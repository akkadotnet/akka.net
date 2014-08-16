using System;
using System.Threading;

namespace Akka.TestKit.Internals
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class TimeSpanExtensions
    {
        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> has no value.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsUndefined(this TimeSpan? timeSpan)
        {
            return !timeSpan.HasValue;
        }

        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> <c>== 0</c>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsZero(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks == 0;
        }

        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> <c>== 0</c>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsZero(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks == 0;
        }




        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> <c>&gt;= 0</c>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsPositiveFinite(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks >= 0;
        }

        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> <c>&gt;= 0</c>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsPositiveFinite(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks >= 0;
        }



        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> is negative.
        /// This is a relaxed definition of when a <see cref="TimeSpan"/>.
        /// Use <see cref="IsInfiniteTimeout(System.TimeSpan)"/> to test using the stricter definition.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsInfinite(this TimeSpan timeSpan)
        {
            return timeSpan.Ticks < 0;
        }

        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> is negative.
        /// This is a relaxed definition of when a <see cref="TimeSpan"/>.
        /// Use <see cref="IsInfiniteTimeout(System.TimeSpan)"/> to test using the stricter definition.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsInfinite(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value.Ticks < 0;
        }



        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> equals <see cref="Timeout.InfiniteTimeSpan"/> 
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsInfiniteTimeout(this TimeSpan timeSpan)
        {
            return timeSpan == Timeout.InfiniteTimeSpan;
        }
        
        /// <summary>
        /// Returns <c>true</c> if the <paramref name="timeSpan"/> equals <see cref="Timeout.InfiniteTimeSpan"/> 
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static bool IsInfiniteTimeout(this TimeSpan? timeSpan)
        {
            return timeSpan.HasValue && timeSpan.Value == Timeout.InfiniteTimeSpan;
        }



        /// <summary>
        /// Throws an <see cref="ArgumentException"/> if the <paramref name="timeSpan"/> is not 0 or greater.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static void EnsureIsPositiveFinite(this TimeSpan timeSpan, string parameterName)
        {
            if(!IsPositiveFinite(timeSpan))
                throw new ArgumentException("The timespan must be >0. Actual value: " + timeSpan, parameterName);
        }

        /// <summary>
        /// Returns the smallest value.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static TimeSpan Min(this TimeSpan a, TimeSpan b)
        {
            if(b.IsInfinite()) return a;
            if(a.IsInfinite()) return b;
            return a < b ? a : b;
        }

        /// <summary>
        /// Returns the smallest value. if <paramref name="b"/> is <c>null</c> it's treated as 
        /// undefined, and <paramref name="a"/> is returned.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
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