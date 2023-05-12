//-----------------------------------------------------------------------
// <copyright file="ShardingEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#nullable enable
using System;
using Akka.Actor;

namespace Akka.Cluster.Sharding;

/// <summary>
/// <para>Default envelope type that may be used with Cluster Sharding.</para>
/// <para>
/// The alternative way of routing messages through sharding is to not use envelopes,
/// and have the message types themselves carry identifiers.
/// </para>
/// </summary>
public sealed class ShardingEnvelope: IWrappedMessage, IClusterShardingSerializable, IEquatable<ShardingEnvelope>
{
    public string EntityId { get; }
    public object Message { get; }

    public ShardingEnvelope(string entityId, object message)
    {
        EntityId = entityId;
        Message = message;
    }
    
    public override string ToString() => $"ShardingEnvelope({EntityId}, {Message})";

    public bool Equals(ShardingEnvelope? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityId == other.EntityId && Message.Equals(other.Message);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is ShardingEnvelope other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (EntityId.GetHashCode() * 397) ^ Message.GetHashCode();
        }
    }
}