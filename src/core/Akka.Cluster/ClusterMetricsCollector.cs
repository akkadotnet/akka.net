//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

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
    /// Metrics sampling is delegated to the <see cref="IMetricsCollector"/>.
    ///
    /// Smoothing of the data for each monitored process is delegated to the
    /// <see cref="EWMA"/> for exponential weighted moving average.
    /// </summary>
    internal class ClusterMetricsCollector : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// The node ring gossiped that contains only members that are <see cref="MemberStatus.Up"/>
        /// </summary>
        public ImmutableHashSet<Address> Nodes { get; private set; }

        /// <summary>
        /// The metrics collector that samples data on the node.
        /// </summary>
        public IMetricsCollector Collector { get; private set; }

        /// <summary>
        /// The latest metric values with their statistical data
        /// </summary>
        public MetricsGossip LatestGossip { get; private set; }

        /// <summary>
        /// Start periodic gossip to random nodes in the cluster
        /// </summary>
        private ICancelable _gossipCancelable;

        /// <summary>
        /// Start periodic metrics collection
        /// </summary>
        private ICancelable _metricsCancelable;

        private Cluster _cluster;

        private readonly IActorRef _publisher;

        public ClusterMetricsCollector(IActorRef publisher)
        {
            _publisher = publisher;
            _cluster = Cluster.Get(Context.System);
            Collector = MetricsCollector.Get(Context.System.AsInstanceOf<ExtendedActorSystem>(), _cluster.Settings);
            LatestGossip = MetricsGossip.Empty;
            Nodes = ImmutableHashSet.Create<Address>();

            _metricsCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                _cluster.Settings.PeriodicTasksInitialDelay.Max(_cluster.Settings.MetricsInterval),
                _cluster.Settings.MetricsInterval, Self, InternalClusterAction.MetricsTick.Instance, Self);

            _gossipCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                _cluster.Settings.PeriodicTasksInitialDelay.Max(_cluster.Settings.GossipInterval), 
                _cluster.Settings.GossipInterval, Self, InternalClusterAction.GossipTick.Instance, Self);

            Receive<InternalClusterAction.GossipTick>(tick => Gossip());
            Receive<InternalClusterAction.MetricsTick>(tick => Collect());
            Receive<MetricsGossipEnvelope>(envelope => ReceiveGossip(envelope));
            Receive<ClusterEvent.CurrentClusterState>(state => ReceiveState(state));
            Receive<ClusterEvent.MemberUp>(up => AddMember(up.Member));
            Receive<ClusterEvent.MemberRemoved>(removed => RemoveMember(removed.Member));
            Receive<ClusterEvent.MemberExited>(exited => RemoveMember(exited.Member));
            Receive<ClusterEvent.UnreachableMember>(member => RemoveMember(member.Member));
            Receive<ClusterEvent.ReachableMember>(member =>
            {
                if (member.Member.Status == MemberStatus.Up) AddMember(member.Member);
            });
            Receive<ClusterEvent.IMemberEvent>(@event => { }); //not interested in other types of member event
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self,new []{ typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.ReachabilityEvent) });
            _cluster.LogInfo("Metrics collection has started successfully.");
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            _gossipCancelable.Cancel();
            _metricsCancelable.Cancel();
            Collector.Dispose();
        }

        /// <summary>
        /// Adds a member to the node ring.
        /// </summary>
        private void AddMember(Member member)
        {
            Nodes = Nodes.Add(member.Address);
        }

        /// <summary>
        /// Removes a member from the node ring.
        /// </summary>
        private void RemoveMember(Member member)
        {
            Nodes = Nodes.Remove(member.Address);
            LatestGossip = LatestGossip.Remove(member.Address);
        }

        /// <summary>
        /// Update the initial node ring for those nodes that are <see cref="MemberStatus.Up"/>
        /// </summary>
        private void ReceiveState(ClusterEvent.CurrentClusterState state)
        {
            Nodes = state.Members.Where(x => x.Status == MemberStatus.Up).Select(x => x.Address).ToImmutableHashSet();
        }

        /// <summary>
        /// Samples the latest metrics for the node, updates metrics statistics in <see cref="MetricsGossip"/>, and
        /// publishes the changes to the event bus.
        /// </summary>
        private void Collect()
        {
            LatestGossip = LatestGossip + Collector.Sample();
            Publish();
        }

        /// <summary>
        /// Receives changes from peer nodes, merges remote with local gossip nodes, then publishes
        /// changes to the event stream for load balancing router consumption, and gossip back.
        /// </summary>
        private void ReceiveGossip(MetricsGossipEnvelope envelope)
        {
            // remote node might not have same view of member nodes, this side should only care
            // about nodes that are known here, otherwise removed nodes can come back
            var otherGossip = envelope.Gossip.Filter(Nodes);
            LatestGossip = LatestGossip.Merge(otherGossip);
            // changes will be published in the period collect task
            if (!envelope.Reply)
                ReplyGossipTo(envelope.From);
        }

        /* GOSSIP TO PEERS */

        private void Gossip()
        {
            var targetAddress = SelectRandomNode(Nodes.Remove(_cluster.SelfAddress).ToImmutableList());
            if (targetAddress == null) return;
            GossipTo(targetAddress);
        }

        private void ReplyGossipTo(Address address)
        {
            SendGossip(address, new MetricsGossipEnvelope(_cluster.SelfAddress, LatestGossip, true));
        }

        private void SendGossip(Address address, MetricsGossipEnvelope envelope)
        {
            Context.ActorSelection(Self.Path.ToStringWithAddress(address)).Tell(envelope);
        }

        private void GossipTo(Address address)
        {
            SendGossip(address, new MetricsGossipEnvelope(_cluster.SelfAddress, LatestGossip, false));
        }

        private Address SelectRandomNode(ImmutableList<Address> addresses)
        {
            if (addresses.IsEmpty) return null;
            return addresses[ThreadLocalRandom.Current.Next(addresses.Count - 1)];
        }

        /// <summary>
        /// Publishes to the event stream.
        /// </summary>
        private void Publish()
        {
            _publisher.Tell(new InternalClusterAction.PublishEvent(new ClusterEvent.ClusterMetricsChanged(LatestGossip.Nodes)));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class MetricsGossip
    {
        public MetricsGossip(ImmutableHashSet<NodeMetrics> nodes)
        {
            Nodes = nodes;
        }

        public ImmutableHashSet<NodeMetrics> Nodes { get; private set; }

        public MetricsGossip Copy(ImmutableHashSet<NodeMetrics> nodes = null)
        {
            return nodes == null ? new MetricsGossip(Nodes.ToImmutableHashSet()) : new MetricsGossip(nodes);
        }

        /// <summary>
        /// Remove nodes if their correlating node ring members are not <see cref="MemberStatus.Up"/>
        /// </summary>
        public MetricsGossip Remove(Address node)
        {
            return Copy(Nodes.Where(n => n.Address != node).ToImmutableHashSet());
        }

        /// <summary>
        /// Only the nodes that are in the <paramref name="includeNodes"/> set.
        /// </summary>
        public MetricsGossip Filter(ImmutableHashSet<Address> includeNodes)
        {
            return Copy(Nodes.Where(x => includeNodes.Contains(x.Address)).ToImmutableHashSet());
        }

        /// <summary>
        /// Adds new remote <see cref="NodeMetrics"/> and merges existing from a remote gossip.
        /// </summary>
        public MetricsGossip Merge(MetricsGossip otherGossip)
        {
            return otherGossip.Nodes.Aggregate(this, (gossip, metrics) => gossip + metrics);
        }

        /// <summary>
        /// Returns <see cref="NodeMetrics"/> for a node if exists.
        /// </summary>
        public NodeMetrics NodeMetricsFor(Address address)
        {
            return Nodes.FirstOrDefault(x => x.Address == address);
        }

        #region Operators

        /// <summary>
        /// Adds new local <see cref="NodeMetrics"/> or merges an existing one.
        /// </summary>
        public static MetricsGossip operator +(MetricsGossip original, NodeMetrics newNode)
        {
            var existingNodeMetrics = original.NodeMetricsFor(newNode.Address);
            return original.Copy(existingNodeMetrics != null ? 
                original.Nodes.Remove(existingNodeMetrics).Add(existingNodeMetrics.Merge(newNode)) : 
                original.Nodes.Add(newNode));
        }

        #endregion

        #region Static members

        public static readonly MetricsGossip Empty = new MetricsGossip(ImmutableHashSet.Create<NodeMetrics>());

        #endregion

        private bool Equals(MetricsGossip other)
        {
            return Nodes.SequenceEqual(other.Nodes);
        }

        public override int GetHashCode()
        {
            return (Nodes != null ? Nodes.GetHashCode() : 0);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MetricsGossip) obj);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Envelope adding a sender address to the gossip.
    /// </summary>
    internal sealed class MetricsGossipEnvelope : IClusterMessage
    {
        public MetricsGossipEnvelope(Address @from, MetricsGossip gossip, bool reply)
        {
            Reply = reply;
            Gossip = gossip;
            From = @from;
        }

        public Address From { get; private set; }

        public MetricsGossip Gossip { get; private set; }

        public bool Reply { get; private set; }

        private bool Equals(MetricsGossipEnvelope other)
        {
            return From.Equals(other.From) && Gossip.Equals(other.Gossip) && Reply.Equals(other.Reply);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = From.GetHashCode();
                hashCode = (hashCode * 397) ^ Gossip.GetHashCode();
                hashCode = (hashCode * 397) ^ Reply.GetHashCode();
                return hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is MetricsGossipEnvelope && Equals((MetricsGossipEnvelope) obj);
        }
    }

    /// <summary>
    /// The snapshot of current sampled health metrics for any monitored process.
    /// Collected and gossiped at regular intervals for dynamic cluster management strategies.
    /// 
    /// Equality of <see cref="NodeMetrics"/> is based on its <see cref="Address"/>.
    /// </summary>
    public class NodeMetrics
    {
        public Address Address { get; private set; }

        /// <summary>
        /// DateTime.Ticks
        /// </summary>
        public long Timestamp { get; private set; }
        public ImmutableHashSet<Metric> Metrics { get; private set; }

        public NodeMetrics(Address address, long timestamp, ImmutableHashSet<Metric> metrics)
        {
            Address = address;
            Timestamp = timestamp;
            Metrics = metrics;
        }

        public NodeMetrics(Address address, long timestamp) : this(address, timestamp, ImmutableHashSet.Create<Metric>()) { }

        /// <summary>
        /// Return the metric that matches <paramref name="key"/>. Returns null if not found.
        /// </summary>
        public Metric Metric(string key)
        {
            return Metrics.FirstOrDefault(metric => metric.Name.Equals(key));
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((NodeMetrics) obj);
        }

        protected bool Equals(NodeMetrics other)
        {
            return Address.Equals(other.Address);
        }

        public override int GetHashCode()
        {
            return Address.GetHashCode();
        }

        /// <summary>
        /// Returns the most recent data
        /// </summary>
        public NodeMetrics Merge(NodeMetrics that)
        {
            if(Address != that.Address) throw new ArgumentException(string.Format("NodeMetrics.merge is only allowed for the same address. {0} != {1}", Address, that.Address));
            if (Timestamp >= that.Timestamp) return this; //that is older
            return new NodeMetrics(Address, that.Timestamp, that.Metrics.Union(Metrics));
        }

        public NodeMetrics Copy(Address address = null, long? timestamp = null, ImmutableHashSet<Metric> metrics = null)
        {
            return new NodeMetrics(address ?? Address, timestamp.HasValue ? timestamp.Value : Timestamp, metrics ?? Metrics);
        }
    }

    /// <summary>
    /// Metrics key/value
    /// 
    /// Equality of metric based on its name
    /// </summary>
    public sealed class Metric : MetricNumericConverter
    {
        public Metric(string name, double value, EWMA average = null)
        {
            Average = average;
            Value = value;
            Name = name;
            if (string.IsNullOrEmpty(Name)) throw new ArgumentNullException("name", string.Format("Invalid Metric {0} value {1}", name, value));
        }

        public string Name { get; private set; }

        public double Value { get; private set; }

        /// <summary>
        /// Can be null
        /// </summary>
        public EWMA Average { get; private set; }

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
            return obj is Metric && Equals((Metric)obj);
        }

        #endregion

        #region Operators

        public static Metric operator +(Metric original, Metric latest)
        {
            if (original.Equals(latest))
            {
                if (original.Average != null) return new Metric(original.Name, latest.Value, original.Average + latest.Value);
                if (latest.Average != null) return new Metric(original.Name, latest.Value, latest.Average);
                return new Metric(original.Name, latest.Value);
            }
            return original;
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Creates a new <see cref="Metric"/> instance if <paramref name="value"/> is valid, otherwise
        /// returns null. Invalid numeric values are negative and NaN/Infinite.
        /// </summary>
        public static Metric Create(string name, double value, double? decayFactor = null)
        {
            return Defined(value) ? new Metric(name, value, CreateEWMA(value, decayFactor)) : null;
        }

        // ReSharper disable once InconsistentNaming
        public static EWMA CreateEWMA(double value, double? decayFactor = null)
        {
            return decayFactor.HasValue ? new EWMA(value, decayFactor.Value) : null;
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Encapsulates evaluation of validity of metric values, conversion of an actual metric value to
    /// an <see cref="Metric"/> for consumption by subscribed cluster entities.
    /// </summary>
    public abstract class MetricNumericConverter
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
    public sealed class EWMA
    {
        public EWMA(double value, double alpha)
        {
            Alpha = alpha;
            Value = value;
            if (!(0.0 <= alpha && alpha <= 1.0)) throw new ArgumentOutOfRangeException("alpha", "alpha must be between 0.0 and 1.0");
        }

        public double Value { get; private set; }

        public double Alpha { get; private set; }

        #region Operators

        public static EWMA operator +(EWMA ewma, double xn)
        {
            var newValue = (ewma.Alpha * xn) + (1 - ewma.Alpha) * ewma.Value;
            if (newValue == ewma.Value) return ewma;
            return new EWMA(newValue, ewma.Alpha);
        }

        #endregion

        #region Static members

        /// <summary>
        /// Math.Log(2)
        /// </summary>
        public const double LogOf2 = 0.69315D;


        ///<summary>
        /// Calculate the alpha (decay factor) used in <see cref="EWMA"/>
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
    internal static class StandardMetrics
    {
        // Constants for memory-related Metric names (accounting for differences between JVM and .NET)
        public const string SystemMemoryMax = "system-memory-max";
        public const string ClrProcessMemoryUsed = "clr-process-memory-used"; //memory for the individual .NET process running Akka.NET
        public const string SystemMemoryAvailable = "system-memory-available";

        //Constants for cpu-related Metric names
        public const string SystemLoadAverage = "system-load-average";
        public const string Processors = "processors";
        public const string CpuCombined = "cpu-combined";

        public static long NewTimestamp()
        {
            return DateTime.UtcNow.Ticks;
        }

        public sealed class SystemMemory
        {
            public Address Address { get; private set; }
            public long Timestamp { get; private set; }
            public long Used { get; private set; }
            public long Available { get; private set; }
            public long? Max { get; private set; }

            public SystemMemory(Address address, long timestamp, long used, long available, long? max = null)
            {
                Address = address;
                Timestamp = timestamp;
                Used = used;
                Available = available;
                Max = max;

                if (!(used > 0L)) throw new ArgumentOutOfRangeException("used", "CLR heap memory expected to be > 0 bytes");
                if (Max.HasValue && !(Max.Value > 0)) throw new ArgumentOutOfRangeException("max", "system max memory expected to be > 0 bytes");
            }

            #region Static methods

            public static SystemMemory ExtractSystemMemory(NodeMetrics nodeMetrics)
            {
                var used = nodeMetrics.Metric(ClrProcessMemoryUsed);
                var available = nodeMetrics.Metric(SystemMemoryAvailable);
                if (used == null || available == null) return null;
                var max = nodeMetrics.Metric(SystemMemoryAvailable) != null ? (long?)Convert.ToInt64(nodeMetrics.Metric(SystemMemoryMax).SmoothValue) : null;
                return new SystemMemory(nodeMetrics.Address, nodeMetrics.Timestamp, 
                    Convert.ToInt64(used.SmoothValue), Convert.ToInt64(available.SmoothValue), max);
            }

            #endregion
        }

        /**
        * @param address <see cref="Akka.Actor.Address"/> of the node the metrics are gathered at
        * @param timestamp the time of sampling, in milliseconds since midnight, January 1, 1970 UTC
        * @param systemLoadAverage OS-specific average load on the CPUs in the system, for the past 1 minute,
        *    The system is possibly nearing a bottleneck if the system load average is nearing number of cpus/cores.
        * @param cpuCombined combined CPU sum of User + Sys + Nice + Wait, in percentage ([0.0 - 1.0]. This
        *   metric can describe the amount of time the CPU spent executing code during n-interval and how
        *   much more it could theoretically.
        * @param processors the number of available processors
        */
        public sealed class Cpu
        {
            public Address Address { get; private set; }
            public long Timestamp { get; private set; }
            public int Cores { get; private set; }
            public double? SystemLoadAverageMeasurement { get; private set; }
            public double? CpuCombinedMeasurement { get; private set; }

            public Cpu(Address address, long timestamp, int cores, double? systemLoadAverage = null, double? cpuCombined = null)
            {
                Address = address;
                Timestamp = timestamp;
                Cores = cores;
                SystemLoadAverageMeasurement = systemLoadAverage;
                CpuCombinedMeasurement = cpuCombined;
            }

            #region Static methods

            /// <summary>
            /// Given a <see cref="NodeMetrics"/> it returns the <see cref="Cpu"/> data of the nodeMetrics
            /// contains the necessary cpu metrics.
            /// </summary>
            public static Cpu ExtractCpu(NodeMetrics nodeMetrics)
            {
                var processors = nodeMetrics.Metric(Processors);
                if (processors == null) return null;
               var systemLoadAverage = nodeMetrics.Metric(SystemLoadAverage) != null ? (double?)nodeMetrics.Metric(SystemLoadAverage).SmoothValue : null;
               var cpuCombined = nodeMetrics.Metric(CpuCombined) != null
                    ? (double?)nodeMetrics.Metric(CpuCombined).SmoothValue
                    : null;

                return new Cpu(nodeMetrics.Address, nodeMetrics.Timestamp, Convert.ToInt32(processors.Value), systemLoadAverage, cpuCombined);
            }

            #endregion
        }
    }

    /// <summary>
    /// Implementations of cluster system metrics implement this interface
    /// </summary>
    public interface IMetricsCollector : IDisposable
    {
        /// <summary>
        /// Sample and collects new data points.
        /// This method is invoked periodically and should return
        /// current metrics for this node.
        /// </summary>
        NodeMetrics Sample();
    }

    /// <summary>
    /// Loads Windows system metrics through Windows Performance Counters
    /// </summary>
    internal class PerformanceCounterMetricsCollector : IMetricsCollector
    {
        public PerformanceCounterMetricsCollector(Address address, double decayFactor)
        {
            DecayFactor = decayFactor;
            Address = address;
        }

        private PerformanceCounterMetricsCollector(Cluster cluster) : this(cluster.SelfAddress,
            EWMA.CalculateAlpha(cluster.Settings.MetricsMovingAverageHalfLife, cluster.Settings.MetricsInterval)) { }

        /// <summary>
        /// This constructor is used when creating an instance from configured fully-qualified name
        /// </summary>
        public PerformanceCounterMetricsCollector(ActorSystem system) : this(Cluster.Get(system)) { }

        #region Performance counters

        private PerformanceCounter _systemLoadAverageCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
        private PerformanceCounter _systemAvailableMemory = new PerformanceCounter("Memory", "Available MBytes", true);

        private static readonly bool IsRunningOnMono = Type.GetType("Mono.Runtime") != null;


        // Mono doesn't support Microsoft.VisualBasic, so need an alternative way of sampling this value
        // see http://stackoverflow.com/questions/105031/how-do-you-get-total-amount-of-ram-the-computer-has
        private PerformanceCounter _monoSystemMaxMemory = IsRunningOnMono
            ? new PerformanceCounter("Mono Memory", "Total Physical Memory")
            : null;


        #endregion

        public Address Address { get; private set; }

        public Double DecayFactor { get; private set; }

        public ImmutableHashSet<Metric> Metrics()
        {
            return ImmutableHashSet.Create<Metric>(new []{ Processors(), SystemLoadAverage(), SystemMaxMemory(), SystemMemoryAvailable(), ClrProcessMemoryUsed() });
        }

        /// <summary>
        /// Samples and collects new data points.
        /// Create a new instance each time.
        /// </summary>
        public NodeMetrics Sample()
        {
            return new NodeMetrics(Address, StandardMetrics.NewTimestamp(), Metrics());
        }

        #region Metric collection methods

        /// <summary>
        /// Returns the number of available processors. Creates a new instance each time.
        /// </summary>
        public Metric Processors()
        {
            return Metric.Create(StandardMetrics.Processors, Environment.ProcessorCount, null);
        }

        /// <summary>
        /// Returns the system load average. Creates a new instance each time.
        /// </summary>
        public Metric SystemLoadAverage()
        {
            return Metric.Create(StandardMetrics.SystemLoadAverage, _systemLoadAverageCounter.NextValue());
        }
        
        /// <summary>
        /// Gets the amount of memory used by this particular CLR process. Creates a new instance each time.
        /// </summary>
        public Metric ClrProcessMemoryUsed()
        {
            return Metric.Create(StandardMetrics.ClrProcessMemoryUsed, Process.GetCurrentProcess().WorkingSet64,
                DecayFactor);
        }

        /// <summary>
        /// Gets the amount of system memory available. Creates a new instance each time.
        /// </summary>
        public Metric SystemMemoryAvailable()
        {
            return Metric.Create(StandardMetrics.SystemMemoryAvailable, _systemAvailableMemory.NextValue(), DecayFactor);
        }

        /// <summary>
        /// Gets the total amount of system memory. Creates a new instance each time.
        /// </summary>
        public Metric SystemMaxMemory()
        {
            return Metric.Create(StandardMetrics.SystemMemoryMax,
                IsRunningOnMono
                    ? _monoSystemMaxMemory.RawValue
                    : GetVbTotalPhysicalMemory());
        }

        double GetVbTotalPhysicalMemory()
        {
#if __MonoCS__
            throw new NotImplementedException();
#else
            return new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
#endif
        }

        #endregion


        #region IDisposable members

        public void Dispose()
        {
            _systemAvailableMemory.Dispose();
            _systemLoadAverageCounter.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// Factory to create a configured <see cref="IMetricsCollector"/>.
    /// </summary>
    internal static class MetricsCollector
    {
        public static IMetricsCollector Get(ExtendedActorSystem system, ClusterSettings settings)
        {
            var fqcn = settings.MetricsCollectorClass;
            if (fqcn == typeof (PerformanceCounterMetricsCollector).AssemblyQualifiedName) return new PerformanceCounterMetricsCollector(system);
            
            var metricsCollectorClass = Type.GetType(fqcn);
            if (metricsCollectorClass == null)
            {
                throw new ConfigurationException(string.Format("Could not create custom metrics collector {0}", fqcn));
            }

            try
            {
                var metricsCollector = (IMetricsCollector) Activator.CreateInstance(metricsCollectorClass, system);
                return metricsCollector;
            }
            catch (Exception ex)
            {
                throw new ConfigurationException(string.Format("Could not create custom metrics collector {0} because: {1}", fqcn, ex.Message));
            }
        }
    }
}

