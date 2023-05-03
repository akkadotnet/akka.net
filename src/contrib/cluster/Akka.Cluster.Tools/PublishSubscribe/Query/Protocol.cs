//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Tools.PublishSubscribe.Query;

#region Request messages

public sealed class GetLocalPubSubStats
{
    public static readonly GetLocalPubSubStats Instance = new();
    private GetLocalPubSubStats() { }
}

public sealed class GetPubSubStats
{
    public static readonly GetPubSubStats Instance = new();
    private GetPubSubStats() { }
}

public sealed class GetClusterPubSubStats
{
    public static readonly GetClusterPubSubStats Instance = new();
    private GetClusterPubSubStats() { }
}

public sealed class GetLocalPubSubState
{
    public static readonly GetLocalPubSubState Instance = new();
    private GetLocalPubSubState() { }
}

public sealed class GetPubSubState
{
    public static readonly GetPubSubState Instance = new();
    private GetPubSubState() { }
}

public sealed class GetClusterPubSubState
{
    public static readonly GetClusterPubSubState Instance = new();
    private GetClusterPubSubState() { }
}

#endregion

#region Reply messages

public sealed class LocalPubSubStats
{
    public LocalPubSubStats(ImmutableDictionary<string, TopicStats> topics)
    {
        Topics = topics;
    }

    public ImmutableDictionary<string, TopicStats> Topics { get; }
}

public sealed class TopicStats
{
    public TopicStats(string name, int subscriberCount)
    {
        Name = name;
        SubscriberCount = subscriberCount;
    }

    public string Name { get; }
    public int SubscriberCount { get; }
}

public sealed class PubSubStats
{
    public PubSubStats(ImmutableDictionary<Address, LocalPubSubStats> clusterStats)
    {
        ClusterStats = clusterStats;
    }

    public ImmutableDictionary<Address, LocalPubSubStats> ClusterStats { get; }
}

public sealed class LocalPubSubState
{
    public LocalPubSubState(ImmutableDictionary<string, TopicState> topics)
    {
        Topics = topics;
    }

    public ImmutableDictionary<string, TopicState> Topics { get; }
}

public sealed class TopicState
{
    public TopicState(string name, ImmutableList<Address> subscribers)
    {
        Name = name;
        Subscribers = subscribers;
    }

    public string Name { get; }
    public ImmutableList<Address> Subscribers { get; }
}

public sealed class PubSubState
{
    public PubSubState(ImmutableDictionary<Address, LocalPubSubState> clusterStates)
    {
        ClusterStates = clusterStates;
    }

    public ImmutableDictionary<Address, LocalPubSubState> ClusterStates { get; }
}

#endregion