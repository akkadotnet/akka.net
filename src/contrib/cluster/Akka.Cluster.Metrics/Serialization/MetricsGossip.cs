//-----------------------------------------------------------------------
// <copyright file="MetricsGossip.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Annotations;
using Akka.Util;
using Akka.Util.Internal;
using Newtonsoft.Json;

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
        public IImmutableSet<NodeMetrics> Nodes { get; private set; } = ImmutableHashSet<NodeMetrics>.Empty;

        /// <summary>
        /// Empty metrics gossip
        /// </summary>
        public static readonly MetricsGossip Empty = new MetricsGossip(ImmutableHashSet<NodeMetrics>.Empty);

        public MetricsGossip(IImmutableSet<NodeMetrics> nodes)
        {
            Nodes = nodes;
        }
        
        /// <summary>
        /// Removes nodes if their correlating node ring members are not <see cref="MemberStatus"/> `Up`.
        /// </summary>
        public MetricsGossip Remove(Actor.Address node)
        {
            return new MetricsGossip(Nodes.Where(n => !n.Address.Equals(node)).ToImmutableHashSet());
        }

        /// <summary>
        /// Only the nodes that are in the `includeNodes` Set.
        /// </summary>
        public MetricsGossip Filter(IImmutableSet<Actor.Address> includeNodes)
        {
            return new MetricsGossip(Nodes.Where(n => includeNodes.Contains(n.Address)).ToImmutableHashSet());
        }

        /// <summary>
        ///  Adds new remote <see cref="NodeMetrics"/> and merges existing from a remote gossip.
        /// </summary>
        public MetricsGossip Merge(MetricsGossip otherGossip)
        {
            return otherGossip.Nodes.Aggregate(this, (gossip, node) => gossip + node);
        }

        /// <summary>
        /// Adds new local <see cref="NodeMetrics"/>, or merges an existing.
        /// </summary>
        public static MetricsGossip operator +(MetricsGossip gossip, NodeMetrics newNodeMetrics)
        {
            var existingMetrics = gossip.NodeMetricsFor(newNodeMetrics.Address);
            if (existingMetrics.HasValue)
                return new MetricsGossip(gossip.Nodes.Remove(existingMetrics.Value).Add(existingMetrics.Value.Update(newNodeMetrics)));

            return new MetricsGossip(gossip.Nodes.Add(newNodeMetrics));
        }

        /// <summary>
        /// Gets node metrics for given node address
        /// </summary>
        public Option<NodeMetrics> NodeMetricsFor(Actor.Address address)
        {
            var node = Nodes.FirstOrDefault(m => m.Address.Equals(address));
            return node ?? Option<NodeMetrics>.None;
        }
    }
}
