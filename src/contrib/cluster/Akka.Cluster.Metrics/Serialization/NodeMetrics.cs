//-----------------------------------------------------------------------
// <copyright file="NodeMetrics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        public Actor.Address Address { get; private set; }
        
        /// <summary>
        /// Creates new instance of <see cref="NodeMetrics"/>
        /// </summary>
        /// <param name="address">Address of the node the metrics are gathered at</param>
        /// <param name="timestamp">the time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
        /// <param name="metrics">The set of sampled <see cref="Types.Metric"/></param>
        public NodeMetrics(Actor.Address address, long timestamp, IEnumerable<Types.Metric> metrics)
        {
            Address = address;
            timestamp_ = timestamp;
            metrics_ = new RepeatedField<Types.Metric>();
            metrics_.AddRange(metrics);
        }

        /// <summary>
        /// Returns the most recent data.
        /// </summary>
        public NodeMetrics Merge(NodeMetrics that)
        {
            if (!Address.Equals(that.Address))
                throw new ArgumentException(nameof(that), $"merge only allowed for same address, {Address} != {that.Address}");

            if (Timestamp >= that.Timestamp)
                return this; // that is order
            
            return new NodeMetrics(Address, that.Timestamp, that.Metrics.Union(Metrics));
        }

        /// <summary>
        /// Returns the most recent data with <see cref="Types.EWMA"/> averaging.
        /// </summary>
        public NodeMetrics Update(NodeMetrics that)
        {
            if (!Address.Equals(that.Address))
                throw new ArgumentException(nameof(that), $"merge only allowed for same address, {Address} != {that.Address}");
            
            // Apply sample ordering
            var (latestNode, currentNode) = Timestamp >= that.Timestamp ? (this, that) : (that, this);
            
            // Average metrics present in both latest and current.
            var updated = latestNode.Metrics
                .SelectMany(latest => currentNode.Metrics.Select(current => (Latest: latest, Current: current)))
                .Where(pair => pair.Latest.SameAs(pair.Current))
                .Select(pair => pair.Current + pair.Latest)
                .ToList();
            
            // Append metrics missing from either latest or current.
            // Equality is based on the metric's name
            var merged = updated.Union(latestNode.Metrics).Union(currentNode.Metrics);
            
            return new NodeMetrics(Address, latestNode.Timestamp, merged);
        }

        /// <summary>
        /// Gets metric by key
        /// </summary>
        public Option<Types.Metric> Metric(string name) => Metrics.FirstOrDefault(m => m.Name == name) ?? Option<Types.Metric>.None;

        /// <summary>
        /// Returns true if <code>that</code> address is the same as this
        /// </summary>
        public bool SameAs(NodeMetrics that) => Address.Equals(that.Address);
        
        /*
         * Two methods below, Equals and GetHashCode, should be used instead of generated in ClusterMetrics.Messages.g.cs
         * file. Since we do not have an option to not generate those methods for this particular class,
         * just stip them from generated code and paste here, with adding Address property check
         */


        /// <inheritdoc />
        public bool Equals(NodeMetrics other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Address, other.Address);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Address != null ? Address.GetHashCode() : 0);
        }
    }
}
