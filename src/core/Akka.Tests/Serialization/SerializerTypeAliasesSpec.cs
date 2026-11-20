//-----------------------------------------------------------------------
// <copyright file="SerializerTypeAliasesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Tests.Serialization;

/// <summary>Configured legacy type names resolve to native module rows without changing legacy inheritance.</summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class SerializerTypeAliasesSpec : AkkaSpec
{
    private const string NativeAlias = "type-alias-native";
    private const string OtherAlias = "type-alias-other";

    public SerializerTypeAliasesSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_ResolveCopiedLegacyNames_When_TheModuleProvidesANativeReplacement")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Should_ResolveCopiedLegacyNames_When_TheModuleProvidesANativeReplacement(bool dynamicTypeLoading, bool assemblyQualified)
    {
        var legacyName = assemblyQualified ? typeof(LegacySerializer).AssemblyQualifiedName : $"{typeof(LegacySerializer).FullName}, Akka.Tests";
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.serializers.copied-legacy = "{{legacyName}}"
            akka.actor.serialization-bindings {
              "{{typeof(Message).FullName}}, Akka.Tests" = copied-legacy
            }
            """);
        // The normal test system boots with dynamic loading on. Build the same supplied rows
        // through the fake module table under either feature setting, as real modules do.
        var system = ActorSystem.Create("serializer-type-alias", config);
        InitializeLogger(system);
        try
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(dynamicTypeLoading, () =>
            {
                var table = new ModuleSerializerTable(new Dictionary<string, Func<ModuleSerializers?>>
                {
                    ["Akka.Tests"] = () => new ReplacementModule()
                });
                var serialization = new AkkaSerialization((ExtendedActorSystem)system, table);
                var writer = serialization.FindSerializerFor(new Message());
                writer.Should().BeOfType<NativeSerializer>();
                writer.Identifier.Should().Be(9418);
                serialization.GetSerializerById(9418).Should().BeSameAs(writer);
                serialization.Deserialize(writer.ToBinary(new Message()), 9418, writer.Manifest(new Message())).Should().BeOfType<Message>();
                return Task.CompletedTask;
            });
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RequireAnExactTypeAndAssembly_When_ResolvingModuleTypeAliases")]
    public void Should_RequireAnExactTypeAndAssembly_When_ResolvingModuleTypeAliases()
    {
        var module = LoadedModule.TryCreate(new ReplacementModule(), (ExtendedActorSystem)Sys, out var skew);
        skew.Should().BeNull();
        module.Should().NotBeNull();
        var loaded = module!;
        var original = loaded.FindSerializer(typeof(NativeSerializer).FullName!, "Akka.Tests");
        loaded.FindSerializer(typeof(LegacySerializer).FullName!, "Akka.Tests").Should().BeSameAs(original);
        loaded.FindSerializer(typeof(LegacySerializer).FullName!, "AKKA.TESTS").Should().BeSameAs(original);
        loaded.FindSerializer(typeof(LegacySerializer).FullName!, "Some.Other.Assembly").Should().BeNull();
        loaded.FindSerializer(typeof(LegacySerializer).FullName!, null).Should().BeNull();
        loaded.FindSerializer(typeof(LegacySerializer).FullName!.ToLowerInvariant(), "Akka.Tests").Should().BeNull();
        loaded.FindSerializer(typeof(CustomLegacySerializer).FullName!, "Akka.Tests").Should().BeNull("type aliases never match subclasses");
        loaded.Details.Should().HaveCount(2, "name aliases do not create duplicate rows or ids");
    }

    [Theory(DisplayName = "Should_RejectInvalidAliasDeclarations_When_TheModuleIsBuilt")]
    [InlineData("missing", "missing alias")]
    [InlineData("not-serializer", "not a Serializer")]
    [InlineData("conflict", "conflicting targets")]
    [InlineData("ambiguous", "ambiguous alias")]
    public void Should_RejectInvalidAliasDeclarations_When_TheModuleIsBuilt(string scenario, string expected)
    {
        var aliases = scenario switch
        {
            "missing" => ImmutableDictionary<Type, string>.Empty.Add(typeof(LegacySerializer), "not-registered"),
            "not-serializer" => ImmutableDictionary<Type, string>.Empty.Add(typeof(Message), NativeAlias),
            "conflict" => ImmutableDictionary<Type, string>.Empty.Add(typeof(NativeSerializer), OtherAlias),
            _ => ImmutableDictionary<Type, string>.Empty.Add(typeof(LegacySerializer), NativeAlias)
        };
        Action build = () => LoadedModule.TryCreate(new ReplacementModule(aliases, duplicateAlias: scenario == "ambiguous"),
            (ExtendedActorSystem)Sys, out _);
        build.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }

    [Fact(DisplayName = "Should_AllowAnIdempotentAlias_When_ItNamesTheExistingNativeType")]
    public void Should_AllowAnIdempotentAlias_When_ItNamesTheExistingNativeType()
    {
        var aliases = ImmutableDictionary<Type, string>.Empty.Add(typeof(NativeSerializer), NativeAlias);
        var module = LoadedModule.TryCreate(new ReplacementModule(aliases), (ExtendedActorSystem)Sys, out var skew);
        skew.Should().BeNull();
        module!.FindSerializer(typeof(NativeSerializer).FullName!, "Akka.Tests")!.Serializer.Should().BeOfType<NativeSerializer>();
    }

    public sealed class Message
    {
    }

    public class LegacySerializer(ExtendedActorSystem system) : Serializer(system)
    {
        public override int Identifier => GetType() == typeof(LegacySerializer) ? 9417 : base.Identifier;
        public override bool IncludeManifest => true;
        public override byte[] ToBinary(object obj) => new byte[] { 42 };
        public override object FromBinary(byte[] bytes, Type type) => new Message();
    }

    public sealed class CustomLegacySerializer(ExtendedActorSystem system) : LegacySerializer(system)
    {
    }

    public sealed class NativeSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9418;
        public override string Manifest(object obj) => typeof(Message).FullName! + ", Akka.Tests";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            writer.Write(new byte[] { 42 });
            return 1;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new Message();
    }

    private sealed class OtherNativeSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9419;
        public override string Manifest(object obj) => "other";
        public override int Serialize(object obj, IBufferWriter<byte> writer) => 0;
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new Message();
    }

    private sealed class ReplacementModule(
        ImmutableDictionary<Type, string>? aliases = null, bool duplicateAlias = false) : ModuleSerializers
    {
        public override ImmutableDictionary<Type, string> SerializerTypeAliases { get; } =
            aliases ?? ImmutableDictionary<Type, string>.Empty.Add(typeof(LegacySerializer), NativeAlias);

        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create(NativeAlias, new NativeSerializer(system), ImmutableHashSet.Create(typeof(Message))),
            SerializerDetails.Create(duplicateAlias ? NativeAlias : OtherAlias, new OtherNativeSerializer(system), ImmutableHashSet<Type>.Empty));
    }
}
