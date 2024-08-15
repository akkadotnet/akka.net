// -----------------------------------------------------------------------
//  <copyright file="ShardingEnvelope.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Actor;

namespace Akka.Cluster.Sharding;

/// <summary>
///     <para>Default envelope type that may be used with Cluster Sharding.</para>
///     <para>
///         The alternative way of routing messages through sharding is to not use envelopes,
///         and have the message types themselves carry identifiers.
///     </para>
/// </summary>
public sealed class ShardingEnvelope : IWrappedMessage, IClusterShardingSerializable, IEquatable<ShardingEnvelope>
{
    public ShardingEnvelope(string entityId, object message)
    {
        EntityId = entityId;
        Message = message;
    }

    public string EntityId { get; }

    public bool Equals(ShardingEnvelope? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityId == other.EntityId && Message.Equals(other.Message);
    }

    public object Message { get; }

    public override string ToString()
    {
        return $"ShardingEnvelope({EntityId}, {Message})";
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is ShardingEnvelope other && Equals(other));
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (EntityId.GetHashCode() * 397) ^ Message.GetHashCode();
        }
    }
}