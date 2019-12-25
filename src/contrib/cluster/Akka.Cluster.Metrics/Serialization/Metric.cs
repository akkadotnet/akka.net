// //-----------------------------------------------------------------------
// // <copyright file="Metric.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Cluster.Metrics.Helpers;
using Akka.Dispatch.SysMsg;
using Akka.Util;

namespace Akka.Cluster.Metrics.Serialization
{
    public sealed partial class NodeMetrics
    {
        public static partial class Types
        {
            /// <summary>
            /// Metrics key/value.
            ///
            /// Equality of Metric is based on its name index.
            /// </summary>
            public sealed partial class Metric
            {
                private readonly Option<EWMA> _average;

                /// <summary>
                /// Creates a new Metric instance if the value is valid, otherwise None
                /// is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(int nameIndex, Number value, Option<decimal> decayFactor)
                    => decayFactor.HasValue ? new Metric(nameIndex, value, CreateEWMA(value.Value64, decayFactor)) : Option<Metric>.None;

                /// <summary>
                /// Creates a new Metric instance if the Try is successful and the value is valid,
                /// otherwise None is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(int nameIndex, Try<Number> value, Option<decimal> decayFactor)
                    => value.IsSuccess ? Create(nameIndex, value.Success.Value, decayFactor) : Option<Metric>.None;

                /// <summary>
                /// Creates <see cref="EWMA"/> if decat factor is set, otherwise None is returned
                /// </summary>
                public static Option<EWMA> CreateEWMA(decimal value, Option<decimal> decayFactor)
                    => decayFactor.HasValue ? new EWMA(value, decayFactor.Value) : Option<EWMA>.None;

                /// <summary>
                /// Metrics key/value.
                /// </summary>
                /// <param name="nameIndex">The metric name index</param>
                /// <param name="number">
                /// The metric value, which must be a valid numerical value,
                /// a valid value is neither negative nor NaN/Infinite.
                /// </param>
                /// <param name="average">
                /// The data stream of the metric value, for trending over time. Metrics that are already
                /// averages (e.g. system load average) or finite (e.g. as number of processors), are not trended.
                /// </param>
                public Metric(int nameIndex, Number number, Option<EWMA> average)
                {
                    if (!Defined(number))
                        throw new ArgumentException(nameof(number), $"Invalid metric #{nameIndex} value {number}");
                    _average = average;
                    nameIndex_ = nameIndex;
                    number_ = number;
                    ewma_ = average.HasValue ? average.Value : default(EWMA);
                }

                /// <summary>
                /// Updates the data point, and if defined, updates the data stream (average).
                /// Returns the updated metric.
                /// </summary>
                public Metric Add(Metric latest)
                {
                    if (!latest.SameAs(this))
                        return this;

                    if (_average.HasValue)
                    {
                        return new Metric(this)
                        {
                            number_ = latest.number_,
                            ewma_ = _average.Value + latest.number_.Value64
                        };
                    }
                    else if (latest._average.HasValue)
                    {
                        return new Metric(this)
                        {
                            number_ = latest.number_,
                            ewma_ = latest._average.Value
                        };
                    }
                    else
                    {
                        return new Metric(this)
                        {
                            number_ = latest.number_,
                        };
                    }
                }
                
                /// <summary>
                /// The numerical value of the average, if defined, otherwise the latest value
                /// </summary>
                public double SmoothValue => _average.HasValue ? _average.Value.Value : Number.Value64;

                /// <summary>
                /// Returns true if this value is smoothed
                /// </summary>
                public bool IsSmooth => _average.HasValue;

                /// <summary>
                /// Returns true if <code>that</code> is tracking the same metric as this.
                /// </summary>
                public bool SameAs(Metric that) => NameIndex == that.NameIndex;

                private bool Defined(Number value)
                {
                    switch (value.Type)
                    {
                        case NumberType.Long:
                        case NumberType.Integer:
                        case NumberType.Serialized:
                            return true;
                        case NumberType.Double:
                        case NumberType.Float:
                            return (float)value.Value64 >= 0;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }
    }
    
}