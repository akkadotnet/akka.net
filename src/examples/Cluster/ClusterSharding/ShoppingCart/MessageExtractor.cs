//-----------------------------------------------------------------------
// <copyright file="MessageExtractor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Sharding;

namespace ShoppingCart
{
    #region ExtractorClass
    public sealed class ShardEnvelope
    {
        public string EntityId { get; }
        public object Payload { get; }

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
            => message switch
            {
                ShardRegion.StartEntity start => start.EntityId,
                ShardEnvelope e => e.EntityId,
                _ => null
            };

        public override object EntityMessage(object message)
            => message switch
            {
                ShardEnvelope e => e.Payload,
                _ => message
            };
    }
    #endregion
}