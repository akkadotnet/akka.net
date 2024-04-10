﻿//-----------------------------------------------------------------------
// <copyright file="MessageExtractor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#nullable enable

using Akka.Cluster.Sharding;

namespace ShoppingCart
{
    #region ExtractorClass

    public sealed class MessageExtractor : HashCodeMessageExtractor
    {
        public MessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards)
        {
        }

        public override string? EntityId(object message)
            => message switch
            {
                _ => null
            };

        public override object EntityMessage(object message)
            => message switch
            {
                _ => message
            };
    }
    #endregion
}
