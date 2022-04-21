//-----------------------------------------------------------------------
// <copyright file="RestartSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    public class RestartSettings
    {
        public TimeSpan MinBackoff { get; }
        public TimeSpan MaxBackoff { get; }
        public double RandomFactor { get; }
        public int MaxRestarts { get; }
        public TimeSpan MaxRestartsWithin { get; }

        private RestartSettings(TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor, int maxRestarts, TimeSpan maxRestartsWithin)
        {
            MinBackoff = minBackoff;
            MaxBackoff = maxBackoff;
            RandomFactor = randomFactor;
            MaxRestarts = maxRestarts;
            MaxRestartsWithin = maxRestartsWithin;
        }

        public static RestartSettings Create(TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor) =>
            new RestartSettings(minBackoff, maxBackoff, randomFactor, int.MaxValue, minBackoff);

        /// <summary>
        /// Minimum (initial) duration until the child actor will started again, if it is terminated
        /// </summary>
        public RestartSettings WithMinBackoff(TimeSpan value) => Copy(minBackoff: value);

        /// <summary>
        /// The exponential back-off is capped to this duration
        /// </summary>
        public RestartSettings WithMaxBackoff(TimeSpan value) => Copy(maxBackoff: value);

        /// <summary>
        /// After calculation of the exponential back-off an additional random delay based on this factor is added
        /// * e.g. `0.2` adds up to `20%` delay. In order to skip this additional delay pass in `0`
        /// </summary>
        public RestartSettings WithRandomFactor(double value) => Copy(randomFactor: value);

        /// <summary>
        /// The amount of restarts is capped to `count` within a timeframe of `within`
        /// </summary>
        public RestartSettings WithMaxRestarts(int count, TimeSpan within) => Copy(maxRestarts: count, maxRestartsWithin: within);

        public override string ToString() => $"RestartSettings(" +
            $"minBackoff={MinBackoff}," +
            $"maxBackoff={MaxBackoff}," +
            $"randomFactor={RandomFactor}," +
            $"maxRestarts={MaxRestarts}," +
            $"maxRestartsWithin={MaxRestartsWithin})";

        private RestartSettings Copy(TimeSpan? minBackoff = null, TimeSpan? maxBackoff = null, double? randomFactor = null, int? maxRestarts = null, TimeSpan? maxRestartsWithin = null) =>
            new RestartSettings(minBackoff ?? MinBackoff, maxBackoff ?? MaxBackoff, randomFactor ?? RandomFactor, maxRestarts ?? MaxRestarts, maxRestartsWithin ?? MaxRestartsWithin);
    }
}
