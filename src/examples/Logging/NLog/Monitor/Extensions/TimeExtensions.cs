using System;

namespace Monitor.Extensions
{
    /// <summary>
    /// Simple extensions allowing to make TimeSpan(s) more readable like : 3.Seconds()
    /// </summary>
    public static class TimeExtensions
    {
        public static TimeSpan Milliseconds(this int @int)
        {
            return TimeSpan.FromMilliseconds(@int);
        }

        public static TimeSpan Minutes(this int @int)
        {
            return TimeSpan.FromMinutes(@int);
        }

        public static TimeSpan Seconds(this int @int)
        {
            return TimeSpan.FromSeconds(@int);
        }
    }
}