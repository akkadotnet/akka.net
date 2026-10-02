using System;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.Hosting;

namespace Akka.Cluster.Hosting;

public sealed class ClusterDaemonOptions
{
    public TimeSpan? KeepAliveInterval { get; set; }
    public ClusterShardingSettings? ShardingSettings { get; set; }
    public string? Role { get; set; }
    public object? HandoffStopMessage { get; set; }

    internal Config ToHocon()
    {
        return KeepAliveInterval is not null 
            ? $"akka.cluster.sharded-daemon-process.keep-alive-interval = {KeepAliveInterval.ToHocon()}" 
            : Config.Empty;
    }
}