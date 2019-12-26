// //-----------------------------------------------------------------------
// // <copyright file="NodeMetrics.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Util;
using Google.Protobuf.Collections;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// The snapshot of current sampled health metrics for any monitored process.
    /// Collected and gossipped at regular intervals for dynamic cluster management strategies.
    ///
    /// Equality of NodeMetrics is based on its address.
    /// </summary>
    public sealed partial class NodeMetrics
    {
        public Address Address { get; }

        /// <summary>
        /// Creates new instance of <see cref="NodeMetrics"/>
        /// </summary>
        /// <param name="addressIndex">Index of the address of the node the metrics are gathered at</param>
        /// <param name="timestamp">the time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
        /// <param name="metrics">The set of sampled <see cref="Types.Metric"/></param>
        public NodeMetrics(int addressIndex, long timestamp, RepeatedField<Types.Metric> metrics)
        {
            addressIndex_ = addressIndex;
            timestamp_ = timestamp;
            metrics_ = metrics;
        }
        
        public NodeMetrics(Address address, long timestamp, RepeatedField<Types.Metric> metrics)
        {
            Address = address;
            timestamp_ = timestamp;
            metrics_ = metrics;
        }

        /// <summary>
        /// Returns the most recent data.
        /// </summary>
        public NodeMetrics Merge(NodeMetrics that)
        {
            if (AddressIndex != that.AddressIndex)
                throw new ArgumentException(nameof(that), $"merge only allowed for same address, {AddressIndex} != {that.AddressIndex}");

            if (Timestamp >= that.Timestamp)
                return this; // that is order
            
            return new NodeMetrics(this)
            {
                metrics_ = { that.metrics_.Union(metrics_.Except(that.metrics_)) },
                timestamp_ = that.timestamp_
            };
        }

        /// <summary>
        /// Returns the most recent data with <see cref="Types.EWMA"/> averaging.
        /// </summary>
        public NodeMetrics Update(NodeMetrics that)
        {
            if (AddressIndex != that.AddressIndex)
                throw new ArgumentException(nameof(that), $"merge only allowed for same address, {AddressIndex} != {that.AddressIndex}");
            
            // Apply sample ordering
            var (latestNode, currentNode) = Timestamp >= that.Timestamp ? (this, that) : (that, this);
            
            // Average metrics present in both latest and current.
            var updated = latestNode.metrics_
                .SelectMany(latest => currentNode.metrics_.Select(current => (Latest: latest, Current: current)))
                .Where(pair => pair.Latest.SameAs(pair.Current))
                .Select(pair => pair.Current.Add(pair.Latest))
                .ToList();
            
            // Append metrics missing from either latest or current.
            // Equality is based on the metric's name index
            var merged = updated.Union(latestNode.metrics_.Except(updated))
                .Union(currentNode.metrics_.Except(updated).Except(latestNode.metrics_));
            
            return new NodeMetrics(this)
            {
                metrics_ = { merged },
                timestamp_ = latestNode.timestamp_
            };
        }

        /// <summary>
        /// Gets metric by key
        /// </summary>
        public Option<Types.Metric> Metric(string name) => Metrics.FirstOrDefault(m => m.Name == name) ?? Option<Types.Metric>.None;

        /// <summary>
        /// Returns true if <code>that</code> address is the same as this
        /// </summary>
        public bool SameAs(NodeMetrics that) => addressIndex_ == that.addressIndex_;
    }
}