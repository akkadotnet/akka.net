// //-----------------------------------------------------------------------
// // <copyright file="Metric.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.ComponentModel;
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
                /// <summary>
                /// Metric average value
                /// </summary>
                public Option<EWMA> Average { get; }
                public AnyNumber Value { get; }
                public string Name { get; }

                /// <summary>
                /// Creates a new Metric instance if the value is valid, otherwise None
                /// is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, AnyNumber value, Option<double> decayFactor)
                    => decayFactor.HasValue ? new Metric(name, value, CreateEWMA(value, decayFactor)) : Option<Metric>.None;

                /// <summary>
                /// Creates a new Metric instance if the Try is successful and the value is valid,
                /// otherwise None is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, Try<AnyNumber> value, Option<double> decayFactor)
                    => value.IsSuccess ? Create(name, value.Success.Value, decayFactor) : Option<Metric>.None;

                /// <summary>
                /// Creates <see cref="EWMA"/> if decat factor is set, otherwise None is returned
                /// </summary>
                public static Option<EWMA> CreateEWMA(AnyNumber value, Option<double> decayFactor)
                    => decayFactor.HasValue ? new EWMA(value.DoubleValue, decayFactor.Value) : Option<EWMA>.None;

                /// <summary>
                /// Metrics key/value.
                /// </summary>
                /// <param name="name">The metric name</param>
                /// <param name="decimalNumber">
                /// The metric value, which must be a valid numerical value,
                /// a valid value is neither negative nor NaN/Infinite.
                /// </param>
                /// <param name="average">
                /// The data stream of the metric value, for trending over time. Metrics that are already
                /// averages (e.g. system load average) or finite (e.g. as number of processors), are not trended.
                /// </param>
                public Metric(string name, AnyNumber value, Option<EWMA> average)
                {
                    if (!Defined(value))
                        throw new ArgumentException(nameof(value), $"Invalid metric {name} value {value}");
                        
                    Name = name;
                    Value = value;
                    Average = average;
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

                    if (Average.HasValue)
                    {
                        return new Metric(this)
                        {
                            number_ = latest.number_,
                            ewma_ = Average.Value + latest.Value.DoubleValue
                        };
                    }
                    else if (latest.Average.HasValue)
                    {
                        return new Metric(this)
                        {
                            number_ = latest.number_,
                            ewma_ = latest.Average.Value
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
                public double SmoothValue => Average.HasValue ? Average.Value.Value : Value.DoubleValue;

                /// <summary>
                /// Returns true if this value is smoothed
                /// </summary>
                public bool IsSmooth => Average.HasValue;

                /// <summary>
                /// Returns true if <code>that</code> is tracking the same metric as this.
                /// </summary>
                public bool SameAs(Metric that) => Name == that.Name;

                private bool Defined(AnyNumber value)
                {
                    var n = ConvertNumber(Value);
                    if (n is Left<long, double> left)
                    {
                        return left.Value >= 0;
                    }
                    else if (n is Right<long, double> right)
                    {
                        return right.Value >= 0;
                    }
                    else return false;
                }

                private Either<long, double> ConvertNumber(AnyNumber number)
                {
                    switch (number.Type)
                    {
                        case AnyNumber.NumberType.Int:
                            return new Left<long, double>(Convert.ToInt32(number.DoubleValue));
                        case AnyNumber.NumberType.Long:
                            return new Left<long, double>(Convert.ToInt64(number.DoubleValue));
                        case AnyNumber.NumberType.Float:
                            return new Right<long, double>(Convert.ToSingle(number.DoubleValue));
                        case AnyNumber.NumberType.Double:
                            return new Right<long, double>(number.DoubleValue);
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }
    }
    
}