//-----------------------------------------------------------------------
// <copyright file="MixedBindingPair.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit;

/// <summary>
/// Two actor systems that disagree about how a port's messages are serialized, as the nodes of a cluster do mid-way
/// through a rolling upgrade: one runs V2, the other is pinned to legacy. It takes plain config for each side, so it
/// works with explicit bindings (<see cref="BindTo"/>) today and with the global switch's config later.
/// Nodes are in-process and swap bytes through their <see cref="Akka.Serialization.Serialization"/> extensions.
/// </summary>
public sealed class MixedBindingPair : IAsyncDisposable
{
    private MixedBindingPair(ActorSystem v2Node, ActorSystem legacyNode)
    {
        V2Node = v2Node;
        LegacyNode = legacyNode;
    }

    /// <summary>The node whose config turns V2 on.</summary>
    public ActorSystem V2Node { get; }

    /// <summary>The node whose config pins the types to legacy.</summary>
    public ActorSystem LegacyNode { get; }

    /// <summary>
    /// Starts the nodes <c>name-v2</c> and <c>name-legacy</c>. Each config falls back to <see cref="AkkaSpec.AkkaSpecConfig"/>
    /// and must register the port's serializers.
    /// </summary>
    public static MixedBindingPair Create(string name, Config v2Side, Config legacySide)
    {
        var v2 = ActorSystem.Create(name + "-v2", v2Side.WithFallback(AkkaSpec.AkkaSpecConfig));
        try
        {
            return new MixedBindingPair(v2, ActorSystem.Create(name + "-legacy", legacySide.WithFallback(AkkaSpec.AkkaSpecConfig)));
        }
        catch
        {
            _ = v2.Terminate();
            throw;
        }
    }

    /// <summary>HOCON that binds <paramref name="types"/> to the serializer registered as <paramref name="alias"/>.</summary>
    public static Config BindTo(string alias, params Type[] types)
    {
        var rows = types.Select(t => $@"""{t.FullName}, {t.Assembly.GetName().Name}"" = {alias}");
        return ConfigurationFactory.ParseString($"akka.actor.serialization-bindings {{\n{string.Join("\n", rows)}\n}}");
    }

    /// <summary>
    /// Sends every message both ways, by serializer id and manifest as a remote node would. Checks that the V2 node
    /// writes with <paramref name="v2Id"/>, the legacy node with <paramref name="legacyId"/>, the manifests match
    /// the cases, and the reading node gets back what was sent.
    /// </summary>
    public void AssertRoundTripsBothWays(IEnumerable<V2PortCase> cases, int v2Id, int legacyId)
    {
        foreach (var c in cases)
        {
            Send(V2Node, LegacyNode, v2Id, c, "V2 node to legacy node");
            Send(LegacyNode, V2Node, legacyId, c, "legacy node to V2 node");
        }
    }

    private static void Send(ActorSystem writer, ActorSystem reader, int writerId, V2PortCase c, string stage)
    {
        var serializer = writer.Serialization.FindSerializerFor(c.Message);
        serializer.Identifier.Should().Be(writerId, "{0}: the writing node picks this serializer for {1}", stage, c.Name);

        var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, c.Message);
        manifest.Should().Be(c.Manifest, "{0}: manifest of {1}", stage, c.Name);

        var received = reader.Serialization.Deserialize(serializer.ToBinary(c.Message), serializer.Identifier, manifest);
        V2PortSpec.AssertSame(c, received, stage);
    }

    /// <summary>Terminates both nodes.</summary>
    public async ValueTask DisposeAsync() => await Task.WhenAll(V2Node.Terminate(), LegacyNode.Terminate()).ConfigureAwait(false);
}
