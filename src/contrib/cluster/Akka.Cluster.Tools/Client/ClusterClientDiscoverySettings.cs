// -----------------------------------------------------------------------
//  <copyright file="ClusterClientDiscoverySettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

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
            config.GetString("method"),
            config.GetString("service-name"),
            config.GetString("port-name"),
            config.GetInt("number-of-contacts"),
            config.GetTimeSpan("interval"),
            config.GetDouble("exponential-backoff-random-factor"),
            config.GetTimeSpan("exponential-backoff-max"),
            config.GetTimeSpan("resolve-timeout"),
            config.GetTimeSpan("probe-timeout"));
    }

    public static ClusterClientDiscoverySettings Create(Config clusterClientConfig)
    {
        var config = clusterClientConfig.GetConfig("discovery");
        if (config is null)
            return Empty;

        return new ClusterClientDiscoverySettings(
            config.GetString("method", Empty.DiscoveryMethod),
            config.GetString("service-name", Empty.ServiceName),
            config.GetString("port-name", Empty.PortName),
            config.GetInt("number-of-contacts", Empty.NumberOfContacts),
            config.GetTimeSpan("interval", Empty.Interval),
            config.GetDouble("exponential-backoff-random-factor", Empty.ExponentialBackoffJitter),
            config.GetTimeSpan("exponential-backoff-max", Empty.ExponentialBackoffMax),
            config.GetTimeSpan("resolve-timeout", Empty.ResolveTimeout),
            config.GetTimeSpan("probe-timeout", Empty.ProbeTimeout)
        );
    }
}