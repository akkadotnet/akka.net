// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class CanonicalSerializationSpec : AkkaSpec
{
    private const string LegacyAlias = "canonical-legacy";
    private const string V2Alias = "canonical-legacy-v2";

    public CanonicalSerializationSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_KeepCanonicalLegacyFormat_When_ModuleDeclaresReservedFuture51Takeover")]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task Should_KeepCanonicalLegacyFormat_When_ModuleDeclaresReservedFuture51Takeover(bool enabled, bool pinV2, bool dynamicLoading)
    {
        await WithRegistry(enabled, pinV2, dynamicLoading, "module", registry =>
        {
            var value = new ProtocolValue("state");
            var selected = registry.FindSerializerFor(value);
            Assert.Equal(enabled || pinV2 ? 51 : 11, selected.Identifier);
            var canonical = registry.FindSerializerForCanonicalFormat(value);
            Assert.Same(registry.GetSerializerForAlias(LegacyAlias), canonical);
            Assert.Equal("legacy:state", Encoding.UTF8.GetString(canonical.ToBinary(value)));
            Assert.Equal(value, canonical.FromBinary(canonical.ToBinary(value), "P"));
            Assert.Same(selected, registry.FindSerializerFor(value));
            Assert.Equal(enabled || pinV2 ? "v2:state" : "legacy:state", Encoding.UTF8.GetString(selected.ToBinary(value)));
        });
    }

    [Theory(DisplayName = "Should_KeepExactCustomInstance_When_TypeAndIdMatchBuiltinV2")]
    [InlineData("binding", true)]
    [InlineData("v2-alias", true)]
    [InlineData("binding", false)]
    [InlineData("v2-alias", false)]
    public async Task Should_KeepExactCustomInstance_When_TypeAndIdMatchBuiltinV2(string custom, bool dynamicLoading)
    {
        await WithRegistry(true, false, dynamicLoading, custom, registry =>
        {
            var value = new ProtocolValue("custom");
            var selected = registry.FindSerializerFor(value);
            Assert.IsType<BuiltinV2Serializer>(selected);
            Assert.Equal(51, selected.Identifier);
            Assert.Same(selected, registry.FindSerializerForCanonicalFormat(value));
            Assert.Equal("v2:custom", Encoding.UTF8.GetString(selected.ToBinary(value)));
        });
    }

    [Theory(DisplayName = "Should_ResolveActiveLegacyAlias_When_ApplicationReplacesIt")]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Should_ResolveActiveLegacyAlias_When_ApplicationReplacesIt(bool enabled, bool dynamicLoading)
    {
        await WithRegistry(enabled, true, dynamicLoading, "legacy-alias", registry =>
        {
            var value = new ProtocolValue("replacement");
            Assert.Equal(51, registry.FindSerializerFor(value).Identifier);
            var canonical = registry.FindSerializerForCanonicalFormat(value);
            Assert.Equal(778, canonical.Identifier);
            Assert.Same(registry.GetSerializerForAlias(LegacyAlias), canonical);
            Assert.Equal("custom:replacement", Encoding.UTF8.GetString(canonical.ToBinary(value)));
        });
    }

    [Theory(DisplayName = "Should_KeepUnrelatedAndNullSerializers_When_NoTakeoverApplies")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_KeepUnrelatedAndNullSerializers_When_NoTakeoverApplies(bool dynamicLoading)
    {
        await WithRegistry(true, false, dynamicLoading, "module", registry =>
        {
            var value = new byte[] { 0, 1, 255 };
            Assert.Same(registry.FindSerializerFor(value), registry.FindSerializerForCanonicalFormat(value));
            Assert.Same(registry.FindSerializerFor(null), registry.FindSerializerForCanonicalFormat(null));
        });
    }

    private async Task WithRegistry(bool enabled, bool pinV2, bool dynamicLoading, string custom, Action<AkkaSerialization> assert)
    {
        var pin = pinV2 ? $"akka.actor.serialization-bindings {{ \"{typeof(ProtocolValue).AssemblyQualifiedName}\" = {V2Alias} }}" : "";
        var config = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}\n{pin}");
        var setup = SerializationSetup.Create(system => custom switch
        {
            "binding" => ImmutableHashSet.Create(SerializerDetails.Create("custom-binding", new BuiltinV2Serializer(system),
                ImmutableHashSet.Create(typeof(ProtocolValue)))),
            "v2-alias" => ImmutableHashSet.Create(SerializerDetails.Create(V2Alias, new BuiltinV2Serializer(system), ImmutableHashSet<Type>.Empty)),
            "legacy-alias" => ImmutableHashSet.Create(SerializerDetails.Create(LegacyAlias, new LegacySerializer(system, 778, "custom:"), ImmutableHashSet<Type>.Empty)),
            _ => ImmutableHashSet<SerializerDetails>.Empty
        });
        var system = ActorSystem.Create("canonical-module", ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), setup));
        InitializeLogger(system);
        try
        {
            var table = new ModuleSerializerTable(new Dictionary<string, Func<ModuleSerializers?>> { ["Akka.Tests"] = () => new FakeCrdtModule() });
            await AkkaFeaturesSpec.WithDynamicTypeLoading(dynamicLoading, () =>
            {
                assert(new AkkaSerialization((ExtendedActorSystem)system, table));
                return Task.CompletedTask;
            });
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed record ProtocolValue(string Value);

    private sealed class FakeCrdtModule : ModuleSerializers
    {
        public override ImmutableDictionary<string, string> Supersedes { get; } = ImmutableDictionary<string, string>.Empty.Add(V2Alias, LegacyAlias);
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create(LegacyAlias, new LegacySerializer(system), ImmutableHashSet.Create(typeof(ProtocolValue))),
            SerializerDetails.Create(V2Alias, new BuiltinV2Serializer(system), ImmutableHashSet<Type>.Empty));
    }

    private sealed class LegacySerializer(ExtendedActorSystem system, int id = 11, string prefix = "legacy:") : SerializerWithStringManifest(system)
    {
        public override int Identifier => id;
        public override string Manifest(object o) => "P";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(prefix + ((ProtocolValue)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => Read(bytes, prefix);
    }

    private sealed class BuiltinV2Serializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 51;
        public override string Manifest(object o) => "P";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes("v2:" + ((ProtocolValue)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => Read(bytes.ToArray(), "v2:");
    }

    private static ProtocolValue Read(byte[] bytes, string prefix)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            throw new SerializationException("Canonical test serializer prefix is missing.");
        return new ProtocolValue(text[prefix.Length..]);
    }
}
