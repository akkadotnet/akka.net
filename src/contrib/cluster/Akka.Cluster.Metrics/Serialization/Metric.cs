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
                public decimal DecimalNumber { get; }
                public string Name { get; }

                /// <summary>
                /// Creates a new Metric instance if the value is valid, otherwise None
                /// is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, decimal value, Option<decimal> decayFactor)
                    => decayFactor.HasValue ? new Metric(name, value, CreateEWMA(value, decayFactor)) : Option<Metric>.None;

                /// <summary>
                /// Creates a new Metric instance if the Try is successful and the value is valid,
                /// otherwise None is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, Try<decimal> value, Option<decimal> decayFactor)
                    => value.IsSuccess ? Create(name, value.Success.Value, decayFactor) : Option<Metric>.None;

                /// <summary>
                /// Creates <see cref="EWMA"/> if decat factor is set, otherwise None is returned
                /// </summary>
                public static Option<EWMA> CreateEWMA(decimal value, Option<decimal> decayFactor)
                    => decayFactor.HasValue ? new EWMA(value, decayFactor.Value) : Option<EWMA>.None;

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
                public Metric(string name, decimal decimalNumber, Option<EWMA> average)
                {
                    if (!Defined(decimalNumber))
                        throw new ArgumentException(nameof(decimalNumber), $"Invalid metric {name} value {decimalNumber}");
                        
                    Name = name;
                    DecimalNumber = decimalNumber;
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
                            ewma_ = Average.Value + latest.DecimalNumber
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
                public decimal SmoothValue => Average.HasValue ? (decimal)Average.Value.Value : Number.DecimalValue;

                /// <summary>
                /// Returns true if this value is smoothed
                /// </summary>
                public bool IsSmooth => Average.HasValue;

                /// <summary>
                /// Returns true if <code>that</code> is tracking the same metric as this.
                /// </summary>
                public bool SameAs(Metric that) => Name == that.Name;

                private bool Defined(decimal value) => value >= 0;
            }

            public sealed partial class Number
            {
                /// <summary>
                /// Gets number decimal value
                /// </summary>
                public decimal DecimalValue
                {
                    get
                    {
                        switch (Type)
                        {
                            case NumberType.Long:
                                return BitConverter.ToInt64(Serialized.ToByteArray(), 0);
                            case NumberType.Integer:
                                return BitConverter.ToInt32(Serialized.ToByteArray(), 0);
                            case NumberType.Serialized:
                                throw new ArgumentException($"Not a number {value64_} of type {Type}");
                            case NumberType.Double:
                                return (decimal)BitConverter.ToDouble(Serialized.ToByteArray(), 0);
                            case NumberType.Float:
                                return (decimal)BitConverter.ToSingle(Serialized.ToByteArray(), 0);
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }
        }
    }
    
}