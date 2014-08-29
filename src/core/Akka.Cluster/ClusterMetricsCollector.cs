using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Cluster metrics is primarily for load-balancing of nodes. It controls metrics sampling
    /// at a regular frequency, prepares highly variable data for further analysis by other entities,
    /// and publishes the latest cluster metrics data around the node ring and local eventStream
    /// to assist in determining the need to redirect traffic to the least-loaded nodes.
    ///
    /// Metrics sampling is delegated to the [[akka.cluster.MetricsCollector]].
    ///
    /// Smoothing of the data for each monitored process is delegated to the
    /// [[akka.cluster.EWMA]] for exponential weighted moving average.
    /// </summary>
    internal class ClusterMetricsCollector
    {
    }

    public class NodeMetrics
    {
    }

    /// <summary>
    /// Metrics key/value
    /// 
    /// Equality of metric based on its name
    /// </summary>
    internal sealed class Metric : MetricNumericConverter
    {
        public Metric(string name, double value, EMWA average = null)
        {
            Average = average;
            Value = value;
            Name = name;
            if(string.IsNullOrEmpty(Name)) throw new ArgumentNullException("name", string.Format("Invalid Metric {0} value {1}", name, value));
        }

        public string Name { get; private set; }

        public double Value { get; private set; }

        /// <summary>
        /// Can be null
        /// </summary>
        public EMWA Average { get; private set; }

        /// <summary>
        /// The numerical value of the average, if defined, otherwise the latest value
        /// </summary>
        public double SmoothValue
        {
            get
            {
                return Average != null ? Average.Value : Value;
            }
        }

        /// <summary>
        /// Returns true if the value is smoothed
        /// </summary>
        public bool IsSmooth
        {
            get { return Average != null; }
        }

        #region Equality 

        private bool Equals(Metric other)
        {
            return string.Equals(Name, other.Name);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Metric && Equals((Metric) obj);
        }

        #endregion

        #region Operators

        public static Metric operator +(Metric original, Metric latest)
        {
            if (original.Equals(latest))
            {
                if(original.Average != null) return new Metric(original.Name, latest.Value, original.Average + latest.Value);
                if(latest.Average != null) return new Metric(original.Name, latest.Value, latest.Average);
                return new Metric(original.Name, latest.Value);
            }
            return original;
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Creates a new <see cref="Metric"/> instance if <see cref="value"/> is valid, otherwise
        /// returns null. Invalid numeric values are negative and NaN/Infinite.
        /// </summary>
        public static Metric Create(string name, double value, double? decayFactor = null)
        {
            return Defined(value) ? new Metric(name, value, CreateEWMA(value,decayFactor)) : null;
        }

// ReSharper disable once InconsistentNaming
        public static EMWA CreateEWMA(double value, double? decayFactor = null)
        {
            return decayFactor.HasValue ? new EMWA(value, decayFactor.Value) : null;
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Encapsulates evaluation of validity of metric values, conversion of an actual metric value to
    /// an <see cref="Metric"/> for consumption by subscribed cluster entities.
    /// </summary>
    internal abstract class MetricNumericConverter
    {
        /// <summary>
        /// A defined value is greater than zero and not NaN / Infinity
        /// </summary>
        public static bool Defined(double value)
        {
            return (value >= 0) && !(Double.IsNaN(value) || Double.IsInfinity(value));
        }

        /// <summary>
        /// Here in .NET-istan, we're going to use <see cref="double"/> for all metrics since we
        /// don't have convenient base classes for denoting general numeric types like Scala.
        /// 
        /// If a specific metrics method needs an integral data type, it should convert down from double.
        /// </summary>
        public static double ConvertNumber(object from)
        {
            if (from is double) return (double)from;
            if (from is float) return Convert.ToDouble((float)from);
            if (from is int) return Convert.ToDouble((int)from);
            if (from is uint) return Convert.ToDouble((uint)from);
            if (from is long) return Convert.ToDouble((long)from);
            if (from is ulong) return Convert.ToDouble((ulong)from);
            throw new ArgumentException(string.Format("Not a number [{0}]", from), "from");
        }
    }

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
    /// INTERNAL API
    ///
    /// @param alpha decay factor, sets how quickly the exponential weighting decays for past data compared to new data,
    ///   see http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
    ///
    /// @param value the current exponentially weighted moving average, e.g. Y(n - 1), or,
    ///             the sampled value resulting from the previous smoothing iteration.
    ///             This value is always used as the previous EWMA to calculate the new EWMA.
    ///
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal sealed class EMWA
    {
        public EMWA(double value, double alpha)
        {
            Alpha = alpha;
            Value = value;
            if (!(0.0 <= alpha && alpha <= 1.0)) throw new ArgumentOutOfRangeException("alpha", "alpha must be between 0.0 and 1.0");
        }

        public double Value { get; private set; }

        public double Alpha { get; private set; }

        #region Operators

        public static EMWA operator +(EMWA emwa, double xn)
        {
            var newValue = (emwa.Alpha * xn) + (1 - emwa.Alpha) * emwa.Value;
            if (newValue == emwa.Value) return emwa;
            return new EMWA(newValue, emwa.Alpha);
        }

        #endregion

        #region Static members

        /// <summary>
        /// Math.Log(2)
        /// </summary>
        public const double LogOf2 = 0.69315D;


        ///<summary>
        /// Calculate the alpha (decay factor) used in <see cref="EMWA"/>
        /// from specified half-life and interval between observations.
        /// Half-life is the interval over which the weights decrease by a factor of two.
        /// The relevance of each data sample is halved for every passing half-life duration,
        /// i.e. after 4 times the half-life, a data sample's relevance is reduced to 6% of
        /// its original relevance. The initial relevance of a data sample is given by
        /// 1 – 0.5 ^ (collect-interval / half-life).
        ///</summary>
        public static double CalculateAlpha(TimeSpan halfLife, TimeSpan collectInterval)
        {
            var halfLifeMillis = halfLife.TotalMilliseconds;
            if (halfLifeMillis < 0) throw new ArgumentOutOfRangeException("halfLife", "halfLife must be > 0s");
            var decayRate = LogOf2 / halfLifeMillis;
            return 1 - Math.Exp(-decayRate * collectInterval.TotalMilliseconds);
        }

        #endregion
    }

    /// <summary>
    /// Definitions of the built-in standard metrics
    /// 
    /// The following extractors and data structures make it easy to consume the
    /// <see cref="NodeMetrics"/> in for example load balancers.
    /// </summary>
    static class StandardMetrics
    {
        // Constants for memory-related Metric names
        public const string VirtualMemoryUsed = "virtual-memory-used";
        public const string PhysicalMemoryUsed = "physical-memory-used";
        public const string MaxVirtualMemory = "virtual-memory-max";
        public const string MaxPhysicalMemory = "physical-memory-max";

        //Constants for cpu-related Metric names


    }
}
