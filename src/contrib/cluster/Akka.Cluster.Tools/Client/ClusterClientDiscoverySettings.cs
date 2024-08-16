//-----------------------------------------------------------------------
// <copyright file="ClusterClientDiscoverySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Discovery;

namespace Akka.Cluster.Tools.Client;

#nullable enable
public sealed record ClusterClientDiscoverySettings(
    string? DiscoveryMethod,
    string? ServiceName,
    string? PortName,
    int NumberOfContacts,
    TimeSpan Interval,
    double ExponentialBackoffJitter,
    TimeSpan ExponentialBackoffMax,
    TimeSpan ResolveTimeout,
    TimeSpan ProbeTimeout)
{
    public static readonly ClusterClientDiscoverySettings Empty;

    static ClusterClientDiscoverySettings()
    {
        var config = DiscoveryProvider.DefaultConfiguration();
        Empty = new ClusterClientDiscoverySettings(
            DiscoveryMethod: config.GetString("method"),
            ServiceName: config.GetString("service-name"),
            PortName: config.GetString("port-name"),
            NumberOfContacts: config.GetInt("number-of-contacts"),
            Interval: config.GetTimeSpan("interval"),
            ExponentialBackoffJitter: config.GetDouble("exponential-backoff-random-factor"),
            ExponentialBackoffMax: config.GetTimeSpan("exponential-backoff-max"),
            ResolveTimeout: config.GetTimeSpan("resolve-timeout"),
            ProbeTimeout: config.GetTimeSpan("probe-timeout"));
    }
    
    public static ClusterClientDiscoverySettings Create(Config clusterClientConfig)
    {
        var config = clusterClientConfig.GetConfig("discovery");
        if (config is null)
            return Empty;
        
        return new ClusterClientDiscoverySettings(
            DiscoveryMethod: config.GetString("method", Empty.DiscoveryMethod),
            ServiceName: config.GetString("service-name", Empty.ServiceName),
            PortName: config.GetString("port-name", Empty.PortName),
            NumberOfContacts: config.GetInt("number-of-contacts", Empty.NumberOfContacts),
            Interval: config.GetTimeSpan("interval", Empty.Interval),
            ExponentialBackoffJitter: config.GetDouble("exponential-backoff-random-factor", Empty.ExponentialBackoffJitter),
            ExponentialBackoffMax: config.GetTimeSpan("exponential-backoff-max", Empty.ExponentialBackoffMax),
            ResolveTimeout: config.GetTimeSpan("resolve-timeout", Empty.ResolveTimeout),
            ProbeTimeout: config.GetTimeSpan("probe-timeout", Empty.ProbeTimeout)
        );
    }
}
