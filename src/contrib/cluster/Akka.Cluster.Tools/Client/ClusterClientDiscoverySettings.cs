// -----------------------------------------------------------------------
//  <copyright file="ContactPointDiscoverySettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client;

#nullable enable
public sealed record ClusterClientDiscoverySettings(
    string? DiscoveryMethod,
    string? ActorSystemName,
    string? ServiceName,
    string ReceptionistName,
    string? PortName,
    TimeSpan DiscoveryRetryInterval,
    TimeSpan DiscoveryTimeout)
{
    public static readonly ClusterClientDiscoverySettings Empty = new ("<method>", null, null, "receptionist", null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    
    public static ClusterClientDiscoverySettings Create(Config clusterClientConfig)
    {
        var config = clusterClientConfig.GetConfig("discovery");
        if (config is null)
            return Empty;
        
        return new ClusterClientDiscoverySettings(
            config.GetString("method"),
            config.GetString("actor-system-name"),
            config.GetString("service-name"),
            config.GetString("receptionist-name", "receptionist"),
            config.GetString("port-name"),
            config.GetTimeSpan("discovery-retry-interval", TimeSpan.FromSeconds(1)),
            config.GetTimeSpan("discovery-timeout", TimeSpan.FromSeconds(60))
        );
    }
}