﻿// -----------------------------------------------------------------------
//  <copyright file="ClusterMetricMessages.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Annotations;
using Akka.Event;

namespace Akka.Cluster.Metrics.Serialization;

/// <summary>
///     INTERNAL API.
///     Remote cluster metrics extension messages.
///     Published to cluster members with metrics extension.
/// </summary>
[InternalApi]
public interface IClusterMetricMessage
{
}

/// <summary>
///     INTERNAL API.
///     Envelope adding a sender address to the cluster metrics gossip.
/// </summary>
[InternalApi]
public sealed class MetricsGossipEnvelope : IClusterMetricMessage, IDeadLetterSuppression
{
    /// <summary>
    ///     Creates new instance of <see cref="MetricsGossipEnvelope" />
    /// </summary>
    public MetricsGossipEnvelope(Address fromAddress, MetricsGossip gossip, bool reply)
    {
        FromAddress = fromAddress;
        Gossip = gossip;
        Reply = reply;
    }

    /// <summary>
    ///     Akka's actor address
    /// </summary>
    public Address FromAddress { get; }

    public MetricsGossip Gossip { get; }

    public bool Reply { get; }
}