//-----------------------------------------------------------------------
// <copyright file="Metric.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel;
using Akka.Annotations;
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
                public static Option<Metric> Create(string name, AnyNumber value)
                    => Defined(value) ? new Metric(name, value, CreateEWMA(value, Option<double>.None)) : Option<Metric>.None;

                /// <summary>
                /// Creates a new Metric instance if the value is valid, otherwise None
                /// is returned. Invalid numeric values are negative and NaN/Infinite.
                /// </summary>
                public static Option<Metric> Create(string name, AnyNumber value, Option<double> decayFactor)
                    => Defined(value) ? new Metric(name, value, CreateEWMA(value, decayFactor)) : Option<Metric>.None;

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
                /// <param name="value">
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
                public static Metric operator +(Metric m1, Metric m2)
                {
                    return m1.Add(m2);
                }

                /// <summary>
                /// Updates the data point, and if defined, updates the data stream (average).
                /// Returns the updated metric.
                /// </summary>
                public Metric Add(Metric latest)
                {
                    if (!SameAs(latest))
                        return this;

                    if (Average.HasValue)
                       return new Metric(Name, latest.Value, Average.Value + latest.Value.DoubleValue);

                    if (latest.Average.HasValue)
                        return new Metric(Name, latest.Value, latest.Average);

                    return new Metric(Name, latest.Value, Option<EWMA>.None);
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

                /// <summary>
                /// Internal usage
                /// </summary>
                [InternalApi]
                public static bool Defined(AnyNumber value)
                {
                    var n = ConvertNumber(value);
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
                
                /// <summary>
                /// Internal usage
                /// </summary>
                [InternalApi]
                public static Either<long, double> ConvertNumber(AnyNumber number)
                {
                    switch (number.Type)
                    {
                        case AnyNumber.NumberType.Int:
                            return new Left<long, double>(Convert.ToInt32(number.LongValue));
                        case AnyNumber.NumberType.Long:
                            return new Left<long, double>(number.LongValue);
                        case AnyNumber.NumberType.Float:
                            return new Right<long, double>(Convert.ToSingle(number.DoubleValue));
                        case AnyNumber.NumberType.Double:
                            return new Right<long, double>(number.DoubleValue);
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                
                /*
                 * Two methods below, Equals and GetHashCode, should be used instead of generated in ClusterMetrics.Messages.g.cs
                 * file. Since we do not have an option to not generate those methods for this particular class,
                 * just stip them from generated code and paste here, with adding Address property check
                 */


                /// <inheritdoc />
                public bool Equals(Metric other)
                {
                    if (ReferenceEquals(null, other)) return false;
                    if (ReferenceEquals(this, other)) return true;
                    return Name == other.Name;
                }

                /// <inheritdoc />
                public override int GetHashCode()
                {
                    return (Name != null ? Name.GetHashCode() : 0);
                }
            }
        }
    }
    
}
