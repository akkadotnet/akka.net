// //-----------------------------------------------------------------------
// // <copyright file="Metric.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Annotations;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Metrics gossip message
    /// </summary>
    [InternalApi]
    public sealed partial class MetricsGossip
    {
        /// <summary>
        /// Empty metrics gossip
        /// </summary>
        public static readonly MetricsGossip Empty = new MetricsGossip()
        {
            nodeMetrics_ = { new NodeMetrics[0] }
        };
        
        /// <summary>
        /// Removes nodes if their correlating node ring members are not <see cref="MemberStatus"/> `Up`.
        /// </summary>
        public MetricsGossip Remove(Address node)
        {
            var indexToRemove = AllAddresses.IndexOf(node);
            if (indexToRemove < 0)
                return this;
            
            return new MetricsGossip(this)
            {
                nodeMetrics_ = { this.nodeMetrics_.Where(n => n.AddressIndex != indexToRemove) }
            };
        }

        /// <summary>
        /// Only the nodes that are in the `includeNodes` Set.
        /// </summary>
        public MetricsGossip Filter(IImmutableSet<Address> includeNodes)
        {
            var indexesToInclude = includeNodes.Select(node => AllAddresses.IndexOf(node));
            return new MetricsGossip(this)
            {
                nodeMetrics_ = { this.nodeMetrics_.Where(n => indexesToInclude.Contains(n.AddressIndex)) }
            };
        }

        /// <summary>
        ///  Adds new remote <see cref="NodeMetrics"/> and merges existing from a remote gossip.
        /// </summary>
        public MetricsGossip Merge(MetricsGossip otherGossip)
        {
            return otherGossip.NodeMetrics.Aggregate(this, (gossip, metrics) => gossip.Append(metrics));
        }

        /// <summary>
        /// Adds new local <see cref="NodeMetrics"/>, or merges an existing.
        /// </summary>
        public MetricsGossip Append(NodeMetrics newNodeMetrics)
        {
            var existingMetrics = NodeMetricsFor(newNodeMetrics.AddressIndex);
            if (existingMetrics.HasValue)
            {
                return new MetricsGossip(this)
                {
                    nodeMetrics_ = { this.nodeMetrics_.Where(m => !m.Equals(existingMetrics.Value)).Concat(existingMetrics.Value.Update(newNodeMetrics)) }
                };
            }
            else
            {
                var newMetrics = this.nodeMetrics_.ToList();
                newMetrics.Add(newNodeMetrics);
                return new MetricsGossip(this)
                {
                    nodeMetrics_ = { newMetrics }
                };
            }
        }

        private Option<NodeMetrics> NodeMetricsFor(int addressIndex)
        {
            var metrics = NodeMetrics.FirstOrDefault(m => m.AddressIndex == addressIndex);
            return metrics ?? Option<NodeMetrics>.None;
        }
    }
}