//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocation.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        private readonly ExtendedActorSystem _system;
        private readonly ConcurrentDictionary<string, IExternalShardAllocationClient> _clients = new ConcurrentDictionary<ShardId, IExternalShardAllocationClient>();

        public ExternalShardAllocation(ExtendedActorSystem system)
        {
            _system = system;
        }

        public IExternalShardAllocationClient ClientFor(string typeName) => Client(typeName);

        private IExternalShardAllocationClient Client(string typeName)
        {
            return _clients.GetOrAdd(typeName, key => new Internal.ExternalShardAllocationClientImpl(_system, key));
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
