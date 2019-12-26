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
                private readonly Option<EWMA> _average;
                
                public string Name { get; }

                /// <summary>
                /// Creates a new Metric instance if the value is valid, otherwise None
                /// is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, Number value, Option<decimal> decayFactor)
                    => decayFactor.HasValue ? new Metric(name, value, CreateEWMA(value.DecimalValue, decayFactor)) : Option<Metric>.None;

                /// <summary>
                /// Creates a new Metric instance if the Try is successful and the value is valid,
                /// otherwise None is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, Try<Number> value, Option<decimal> decayFactor)
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
                /// <param name="number">
                /// The metric value, which must be a valid numerical value,
                /// a valid value is neither negative nor NaN/Infinite.
                /// </param>
                /// <param name="average">
                /// The data stream of the metric value, for trending over time. Metrics that are already
                /// averages (e.g. system load average) or finite (e.g. as number of processors), are not trended.
                /// </param>
                public Metric(string name, Number number, Option<EWMA> average)
                {
                    if (!Defined(number))
                        throw new ArgumentException(nameof(number), $"Invalid metric {name} value {number}");
                        
                    Name = name;
                    _average = average;
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
                            ewma_ = _average.Value + latest.number_.DecimalValue
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
                public decimal SmoothValue => _average.HasValue ? (decimal)_average.Value.Value : Number.DecimalValue;

                /// <summary>
                /// Returns true if this value is smoothed
                /// </summary>
                public bool IsSmooth => _average.HasValue;

                /// <summary>
                /// Returns true if <code>that</code> is tracking the same metric as this.
                /// </summary>
                public bool SameAs(Metric that) => Name == that.Name;

                private bool Defined(Number value) => value.DecimalValue >= 0;
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