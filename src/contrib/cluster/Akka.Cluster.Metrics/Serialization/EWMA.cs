//-----------------------------------------------------------------------
// <copyright file="EWMA.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Metrics.Serialization
{
    public sealed partial class NodeMetrics
    {
        public static partial class Types
        {
            /// <summary>
            /// The exponentially weighted moving average (EWMA) approach captures short-term
            /// movements in volatility for a conditional volatility forecasting model. By virtue
            /// of its alpha, or decay factor, this provides a statistical streaming data model
            /// that is exponentially biased towards newer entries.
            /// 
            /// http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
            /// 
            /// An EWMA only needs the most recent forecast value to be kept, as opposed to a standard
            /// moving average model.
            /// 
            /// Parameter 'alpha' decay factor, sets how quickly the exponential weighting decays for past data compared to new data,
            ///   see http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
            /// 
            /// Parameter 'value' the current exponentially weighted moving average, e.g. Y(n - 1), or,
            ///             the sampled value resulting from the previous smoothing iteration.
            ///             This value is always used as the previous EWMA to calculate the new EWMA.
            /// </summary>
            public sealed partial class EWMA 
            {
                /// <summary>
                /// Creates new instance of <see cref="EWMA"/>
                /// </summary>
                /// <param name="value">
                /// The current exponentially weighted moving average, e.g. Y(n - 1), or,
                ///  the sampled value resulting from the previous smoothing iteration.
                /// </param>
                /// <param name="alpha">
                /// Decay factor, sets how quickly the exponential weighting decays for past data compared to new data,
                /// see http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
                /// </param>
                public EWMA(double value, double alpha)
                {
                    if (alpha < 0 || alpha > 1)
                        throw new ArgumentException(nameof(alpha), "alpha must be between 0.0 and 1.0");
                    
                    value_ = value;
                    alpha_ = alpha;
                }

                /// <summary>
                /// Calculates the exponentially weighted moving average for a given monitored data set.
                /// </summary>
                /// <param name="current">Current EWMA value</param>
                /// <param name="xn">The new data point</param>
                /// <returns>A new EWMA with the updated value</returns>
                public static EWMA operator +(EWMA current, double xn)
                {
                    var newValue = (current.Alpha * xn) + (1 - current.Alpha) * current.Value;
                    if (newValue.Equals(current.Value))
                        return current;
                    
                    return new EWMA(newValue, current.Alpha);
                }

                /// <summary>
                /// Calculate the alpha (decay factor) used in [[akka.cluster.EWMA]]
                /// from specified half-life and interval between observations.
                /// Half-life is the interval over which the weights decrease by a factor of two.
                /// The relevance of each data sample is halved for every passing half-life duration,
                /// i.e. after 4 times the half-life, a data sampleâ€™s relevance is reduced to 6% of
                /// its original relevance. The initial relevance of a data sample is given by
                /// 1 â€“ 0.5 ^ (collect-interval / half-life).
                /// </summary>
                public static double GetAlpha(TimeSpan halfLife, TimeSpan collectInterval)
                {
                    const double logOf2 = 0.69315; // math.log(2)
                    
                    var halfLifeMillis = halfLife.TotalMilliseconds;
                    if (halfLifeMillis <= 0)
                        throw new ArgumentException(nameof(halfLife), "halfLife must be > 0 s");

                    var decayRate = logOf2 / halfLifeMillis;
                    return 1 - Math.Exp(-decayRate * collectInterval.TotalMilliseconds);
                }
            }
        }
    }
}
