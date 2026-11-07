// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization;

public sealed class ReplicatorDigestSerializerSpec : AkkaSpec
{
    public ReplicatorDigestSerializerSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_KeepDigestFormatAndStateSensitivity_When_NodesUseOpposingProtocolBindings")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_KeepDigestFormatAndStateSensitivity_When_NodesUseOpposingProtocolBindings(bool customAlias)
    {
        const string name = "replicator-digest";
        var custom = customAlias ? ConfigurationFactory.ParseString($$"""
            akka.actor.serializers.akka-data-replication = "{{typeof(CustomDigestSerializer).AssemblyQualifiedName}}"
            """) : Config.Empty;
        Config ConfigFor(bool v2) => ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(v2 ? "on" : "off")}")
            .WithFallback(custom).WithFallback(ReplicatorMessagePackSerializerSpec.PortConfig);
        var legacyNode = ActorSystem.Create(name, ConfigFor(false));
        var v2Node = ActorSystem.Create(name, ConfigFor(true));
        try
        {
            var first = ReplicatorMessagePackSerializerSpec.Node(name, "first.example", 1L << 40);
            var state = new DataEnvelope(GCounter.Empty.Increment(first, 7));
            var changed = new DataEnvelope(GCounter.Empty.Increment(first, 8));
            var legacy = Replicator.FindDigestSerializer(legacyNode);
            var modern = Replicator.FindDigestSerializer(v2Node);
            Assert.Equal(customAlias ? 778 : 12, legacy.Identifier);
            Assert.Equal(52, v2Node.Serialization.FindSerializerFor(state).Identifier);
            Assert.Equal(legacy.Identifier, modern.Identifier);
            var oldDigest = SHA1.HashData(legacy.ToBinary(state));
            var modernDigest = SHA1.HashData(modern.ToBinary(state));
            Assert.Equal(oldDigest, modernDigest);
            Assert.False(modernDigest.SequenceEqual(SHA1.HashData(modern.ToBinary(changed))));
            Assert.Equal(legacy.ToBinary(state), modern.ToBinary(state));
        }
        finally
        {
            await Task.WhenAll(legacyNode.Terminate(), v2Node.Terminate());
        }
    }

    public sealed class CustomDigestSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("custom-digest-778:");
        private readonly ReplicatorMessageSerializer _legacy = new(system);

        public override int Identifier => 778;
        public override string Manifest(object o) => "H";
        public override byte[] ToBinary(object obj) => Prefix.Concat(_legacy.ToBinary(obj)).ToArray();

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (!bytes.Take(Prefix.Length).SequenceEqual(Prefix))
                throw new SerializationException("Custom digest serializer prefix is missing.");
            return _legacy.FromBinary(bytes.Skip(Prefix.Length).ToArray(), manifest);
        }
    }
}
