// //-----------------------------------------------------------------------
// // <copyright file="ShardingEnvelope.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Sharding;

/// <summary>
/// <para>Default envelope type that may be used with Cluster Sharding.</para>
/// <para>
/// The alternative way of routing messages through sharding is to not use envelopes,
/// and have the message types themselves carry identifiers.
/// </para>
/// </summary>
public sealed class ShardingEnvelope: IWrappedMessage, IClusterShardingSerializable
{
    public string EntityId { get; }
    public object Message { get; }

    public ShardingEnvelope(string entityId, object message)
    {
        EntityId = entityId;
        Message = message;
    }
    
    public override string ToString() => $"ShardingEnvelope({EntityId}, {Message})";
}