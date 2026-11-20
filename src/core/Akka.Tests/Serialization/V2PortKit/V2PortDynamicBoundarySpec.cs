// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using Akka.Util;
using Xunit;
using Xunit.Sdk;

namespace Akka.Tests.Serialization.V2PortKit;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class V2PortDynamicBoundarySpec : AkkaSpec
{
    private const string Switch = "Akka.DynamicTypeLoading";

    public V2PortDynamicBoundarySpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_CheckPositiveAndRejectedShapes_When_DynamicLoadingBoundariesAreDeclared")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_CheckPositiveAndRejectedShapes_When_DynamicLoadingBoundariesAreDeclared(bool previous)
    {
        var hadSwitch = AppContext.TryGetSwitch(Switch, out var initial);
        AppContext.SetSwitch(Switch, previous);
        try
        {
            RunGate("honest");
            Assert.Equal(previous, AkkaFeatures.IsDynamicTypeLoadingSupported);
        }
        finally
        {
            AppContext.SetSwitch(Switch, !hadSwitch || initial);
        }
    }

    [Theory(DisplayName = "Should_RejectBoundaryBypasses_When_AFlaggedCodecWritesOrReadsWithoutTheGuard")]
    [InlineData("array-write")]
    [InlineData("buffer-write")]
    [InlineData("partial-write")]
    [InlineData("id-read")]
    [InlineData("warm-read")]
    [InlineData("warm-array-read")]
    [InlineData("cold-write")]
    [InlineData("cold-partial-write")]
    [InlineData("cold-direct-read")]
    [InlineData("wrong-reason")]
    public void Should_RejectBoundaryBypasses_When_AFlaggedCodecWritesOrReadsWithoutTheGuard(string mode)
    {
        var before = AkkaFeatures.IsDynamicTypeLoadingSupported;
        Assert.ThrowsAny<XunitException>(() => RunGate(mode));
        Assert.Equal(before, AkkaFeatures.IsDynamicTypeLoadingSupported);
    }

    private void RunGate(string mode)
    {
        V2PortCase[] cases =
        [
            new(new BoundaryValue(false), "P", "known"),
            new(new BoundaryValue(true), "P", "generic") { RequiresDynamicTypeLoading = true }
        ];
        Assert.False(cases[0].RequiresDynamicTypeLoading);
        var node = (ExtendedActorSystem)Sys;
        var table = new ModuleSerializerTable(new Dictionary<string, Func<ModuleSerializers?>>
        {
            ["Akka.Tests"] = () => new BoundaryModule(mode)
        });
        V2PortSpec.AssertDynamicTypeLoadingGate(cases, () => new BoundarySerializer(node, mode),
            () => new Akka.Serialization.Serialization(node, table));
    }

    private sealed record BoundaryValue(bool IsGeneric);

    private sealed class BoundaryModule(string mode) : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("boundary-v2", new BoundarySerializer(system, mode), ImmutableHashSet<Type>.Empty));
    }

    private sealed class BoundarySerializer(ExtendedActorSystem system, string mode) : SerializerV2(system)
    {
        private bool _readWhileEnabled;
        private readonly bool _constructedWhileDisabled = !AkkaFeatures.IsDynamicTypeLoadingSupported;
        public override int Identifier => 51;
        public override string Manifest(object obj) => "P";

        public override byte[] ToBinary(object obj)
        {
            if (((BoundaryValue)obj).IsGeneric && !AkkaFeatures.IsDynamicTypeLoadingSupported)
            {
                if (mode == "array-write" || mode == "cold-write" && _constructedWhileDisabled)
                    return [1];
                throw Error();
            }
            return base.ToBinary(obj);
        }

        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var value = (BoundaryValue)obj;
            if (value.IsGeneric && !AkkaFeatures.IsDynamicTypeLoadingSupported &&
                !(mode == "buffer-write" || mode == "cold-write" && _constructedWhileDisabled))
            {
                if (mode == "partial-write" || mode == "cold-partial-write" && _constructedWhileDisabled)
                    writer.Write(new byte[] { 1 });
                throw Error();
            }
            writer.Write(new byte[] { value.IsGeneric ? (byte)1 : (byte)0 });
            return 1;
        }

        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            var value = new BoundaryValue(bytes.FirstSpan[0] == 1);
            if (AkkaFeatures.IsDynamicTypeLoadingSupported)
                _readWhileEnabled = true;
            else if (value.IsGeneric && !(mode == "warm-read" && _readWhileEnabled || mode == "cold-direct-read" && !_readWhileEnabled))
                throw Error();
            return value;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (bytes[0] == 1 && !AkkaFeatures.IsDynamicTypeLoadingSupported)
            {
                if (mode == "id-read" || mode == "warm-array-read" && _readWhileEnabled)
                    return new BoundaryValue(true);
                if (mode == "cold-direct-read")
                    throw Error();
            }
            return base.FromBinary(bytes, manifest);
        }

        private SerializationException Error() => new(mode == "wrong-reason"
            ? "Generic shape is unavailable."
            : "Akka.DynamicTypeLoading is disabled for this generic shape.");
    }
}
