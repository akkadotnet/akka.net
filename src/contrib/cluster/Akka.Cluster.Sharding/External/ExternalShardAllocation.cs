// -----------------------------------------------------------------------
//  <copyright file="ExternalShardAllocation.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Cluster.Sharding.External.Internal;

namespace Akka.Cluster.Sharding.External;

/// <summary>
///     API May Change
/// </summary>
public sealed class ExternalShardAllocation : IExtension
{
    private readonly ConcurrentDictionary<string, IExternalShardAllocationClient> _clients = new();

    private readonly ExtendedActorSystem _system;

    public ExternalShardAllocation(ExtendedActorSystem system)
    {
        _system = system;
    }

    public static ExternalShardAllocation Get(ActorSystem system)
    {
        return system.WithExtension<ExternalShardAllocation, ExternalShardAllocationExtensionProvider>();
    }

    public IExternalShardAllocationClient ClientFor(string typeName)
    {
        return Client(typeName);
    }

    private IExternalShardAllocationClient Client(string typeName)
    {
        return _clients.GetOrAdd(typeName, key => new ExternalShardAllocationClientImpl(_system, key));
    }
}

public class ExternalShardAllocationExtensionProvider : ExtensionIdProvider<ExternalShardAllocation>
{
    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="system">TBD</param>
    /// <returns>TBD</returns>
    public override ExternalShardAllocation CreateExtension(ExtendedActorSystem system)
    {
        var extension = new ExternalShardAllocation(system);
        return extension;
    }
}