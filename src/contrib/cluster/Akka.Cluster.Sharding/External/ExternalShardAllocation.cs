//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocation.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;

namespace Akka.Cluster.Sharding.External
{
    using ShardId = String;

    /// <summary>
    /// API May Change
    /// </summary>
    public sealed class ExternalShardAllocation : IExtension
    {
        public static ExternalShardAllocation Get(ActorSystem system)
        {
            return system.WithExtension<ExternalShardAllocation, ExternalShardAllocationExtensionProvider>();
        }

        private readonly ExtendedActorSystem system;
        private readonly ConcurrentDictionary<string, IExternalShardAllocationClient> clients = new ConcurrentDictionary<ShardId, IExternalShardAllocationClient>();

        public ExternalShardAllocation(ExtendedActorSystem system)
        {
            this.system = system;
        }

        public IExternalShardAllocationClient ClientFor(string typeName) => Client(typeName);

        private IExternalShardAllocationClient Client(string typeName)
        {
            return clients.GetOrAdd(typeName, key => new Internal.ExternalShardAllocationClientImpl(system, key));
        }
    }

    public class ExternalShardAllocationExtensionProvider : ExtensionIdProvider<ExternalShardAllocation>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override ExternalShardAllocation CreateExtension(ExtendedActorSystem system)
        {
            var extension = new ExternalShardAllocation(system);
            return extension;
        }
    }
}
