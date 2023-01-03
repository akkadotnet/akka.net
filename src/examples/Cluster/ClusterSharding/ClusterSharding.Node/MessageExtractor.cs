﻿//-----------------------------------------------------------------------
// <copyright file="MessageExtractor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Sharding;

namespace ClusterSharding.Node
{
    public sealed class ShardEnvelope
    {
        public readonly string EntityId;
        public readonly object Payload;

        public ShardEnvelope(string entityId, object payload)
        {
            EntityId = entityId;
            Payload = payload;
        }
    }

    public sealed class MessageExtractor : HashCodeMessageExtractor
    {
        public MessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards)
        {
        }

        public override string EntityId(object message)
        {
            switch (message)
            {
                case ShardRegion.StartEntity start: return start.EntityId;
                case ShardEnvelope e: return e.EntityId;
            }

            return null;
        }

        public override object EntityMessage(object message)
        {
            switch (message)
            {
                case ShardEnvelope e: return e.Payload;
                default:
                    return message;
            }
        }
    }
}
