//-----------------------------------------------------------------------
// <copyright file="PersistenceSnapshotNativeSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using VerifyXunit;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;
using SnapshotEnvelope = Akka.Persistence.Serialization.Snapshot;

namespace Akka.Persistence.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class PersistenceSnapshotNativeSerializerSpec : AkkaSpec
{
    private const string SnapshotManifest = "Akka.Persistence.Serialization.Snapshot, Akka.Persistence";
    private const string DynamicSwitch = "Akka.DynamicTypeLoading";

    public PersistenceSnapshotNativeSerializerSpec(ITestOutputHelper output) : base(output)
    {
    }

    // Captured before native implementation from original public serializer git blob
    // a6a1c8cf63dc23c275d705a592faa37586ff2f09, unchanged from upstream dev 2d6a06797.
    internal static IReadOnlyList<(string Name, SnapshotEnvelope Snapshot)> Corpus { get; } = new[]
    {
        ("string", new SnapshotEnvelope("snapshot")),
        ("string-empty", new SnapshotEnvelope("")),
        ("bytes", new SnapshotEnvelope(new byte[] { 0, 1, 128, 255 })),
        ("bytes-empty", new SnapshotEnvelope(System.Array.Empty<byte>())),
        ("null", new SnapshotEnvelope(null))
    };

    [Fact(DisplayName = "Should_PreserveHistoricalBytes_When_UsingTheNativeSnapshotImplementation")]
    public async Task Should_PreserveHistoricalBytes_When_UsingTheNativeSnapshotImplementation()
    {
        var native = new NativePersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
        var legacy = new PersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in Corpus)
        {
            native.Identifier.Should().Be(8);
            native.Manifest(sample.Snapshot).Should().Be(legacy.Manifest(sample.Snapshot)).And.Be(SnapshotManifest);
            var expected = legacy.ToBinary(sample.Snapshot);
            native.ToBinary(sample.Snapshot).Should().Equal(expected);
            var buffer = new ArrayBufferWriter<byte>();
            native.Serialize(sample.Snapshot, buffer).Should().Be(expected.Length);
            buffer.WrittenSpan.ToArray().Should().Equal(expected);
            var dump = HexDumpFormatter.Format(sample.Name, "Snapshot", SnapshotManifest, 8, buffer.WrittenSpan.ToArray());
            await Verifier.Verify(dump).UseDirectory("Snapshots")
                .UseFileName($"PersistenceSnapshotNativeSerializerSpec.{sample.Name}.legacy");
            AssertData(native.FromBinary(expected, typeof(SnapshotEnvelope)), sample.Snapshot);
            AssertData(legacy.FromBinary(buffer.WrittenSpan.ToArray(), typeof(SnapshotEnvelope)), sample.Snapshot);
        }
    }

    [Fact(DisplayName = "Should_ReadHistoricalRecordsAtEverySegmentBoundary_When_ProtobufInputIsSegmented")]
    public void Should_ReadHistoricalRecordsAtEverySegmentBoundary_When_ProtobufInputIsSegmented()
    {
        var native = new NativePersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
        var legacy = new PersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in Corpus)
        {
            var bytes = legacy.ToBinary(sample.Snapshot);
            for (var split = 0; split <= bytes.Length; split++)
                AssertData(native.Deserialize(Split(bytes, split), SnapshotManifest), sample.Snapshot);
            var withUnknownField = bytes.Concat(new byte[] { 0x78, 0x01 }).ToArray();
            AssertData(native.Deserialize(new ReadOnlySequence<byte>(withUnknownField), SnapshotManifest), sample.Snapshot);
        }
    }

    [Theory(DisplayName = "Should_ResolveTheNativeDefaultAndCopiedLegacyName_When_DynamicTypeLoadingIsDisabled")]
    [InlineData("none")]
    [InlineData("versioned")]
    [InlineData("stripped")]
    public async Task Should_ResolveTheNativeDefaultAndCopiedLegacyName_When_DynamicTypeLoadingIsDisabled(string spelling)
    {
        var serializerName = spelling == "versioned" ? typeof(PersistenceSnapshotSerializer).AssemblyQualifiedName
            : $"{typeof(PersistenceSnapshotSerializer).FullName}, Akka.Persistence";
        var config = spelling == "none" ? Config.Empty
            : ConfigurationFactory.ParseString($"akka.actor.serializers.akka-persistence-snapshot = \"{serializerName}\"");
        var system = ActorSystem.Create($"snapshot-strict-{spelling}", config);
        InitializeLogger(system);
        try
        {
            var hadSwitch = AppContext.TryGetSwitch(DynamicSwitch, out var previous);
            AppContext.SetSwitch(DynamicSwitch, false);
            try
            {
                var serialization = new AkkaSerialization((ExtendedActorSystem)system);
                var writer = serialization.FindSerializerForType(typeof(SnapshotEnvelope));
                writer.Should().BeOfType<NativePersistenceSnapshotSerializer>();
                serialization.GetSerializerById(8).Should().BeSameAs(writer);
                foreach (var sample in Corpus)
                {
                    var bytes = writer.ToBinary(sample.Snapshot);
                    AssertData(serialization.Deserialize(new ReadOnlySequence<byte>(bytes), 8, SnapshotManifest), sample.Snapshot);
                    const string versioned = "Akka.Persistence.Serialization.Snapshot, Akka.Persistence, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null";
                    AssertData(serialization.Deserialize(bytes, 8, versioned), sample.Snapshot);
                }
            }
            finally
            {
                AppContext.SetSwitch(DynamicSwitch, !hadSwitch || previous);
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_PreserveLegacySubclassOverridesAndIds_When_TheModuleUsesANativeDefault")]
    public async Task Should_PreserveLegacySubclassOverridesAndIds_When_TheModuleUsesANativeDefault()
    {
        var config = ConfigurationFactory.ParseString($"akka.actor.serialization-identifiers {{ \"{typeof(CustomLegacySerializer).FullName}, Akka.Persistence.Tests\" = 9718 }}");
        var system = ActorSystem.Create("snapshot-legacy-subclass", config);
        InitializeLogger(system);
        try
        {
            var original = new PersistenceSnapshotSerializer((ExtendedActorSystem)system);
            original.Identifier.Should().Be(8);
            original.IncludeManifest.Should().BeTrue();
            var custom = new CustomLegacySerializer((ExtendedActorSystem)system);
            custom.Identifier.Should().Be(9718);
            custom.IncludeManifest.Should().BeFalse();
            custom.ToBinary(new SnapshotEnvelope("value")).Should().Equal(new byte[] { 42 });
            custom.FromBinary<SnapshotEnvelope>(new byte[] { 42 }).Data.Should().Be("custom-legacy");
            system.Serialization.FindSerializerFor(new SnapshotEnvelope("value")).Should().BeOfType<NativePersistenceSnapshotSerializer>();
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_PreserveIndependentPayloadSerializers_When_TheEnvelopeUsesNativeBuffers")]
    public async Task Should_PreserveIndependentPayloadSerializers_When_TheEnvelopeUsesNativeBuffers()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("snapshot-user-v1", new UserV1Serializer(system), ImmutableHashSet.Create(typeof(UserV1))),
            SerializerDetails.Create("snapshot-user-v2", new UserV2Serializer(system), ImmutableHashSet.Create(typeof(UserV2))))));
        var system = ActorSystem.Create("snapshot-user-codecs", setup);
        InitializeLogger(system);
        try
        {
            var native = new NativePersistenceSnapshotSerializer((ExtendedActorSystem)system);
            var legacy = new PersistenceSnapshotSerializer((ExtendedActorSystem)system);
            foreach (var payload in new object[] { new UserV1("v1"), new UserV2("v2") })
            {
                var snapshot = new SnapshotEnvelope(payload);
                var bytes = legacy.ToBinary(snapshot);
                var buffer = new ArrayBufferWriter<byte>();
                native.Serialize(snapshot, buffer).Should().Be(bytes.Length);
                buffer.WrittenSpan.ToArray().Should().Equal(bytes);
                AssertData(native.Deserialize(new ReadOnlySequence<byte>(bytes), SnapshotManifest), snapshot);
                AssertData(legacy.FromBinary(buffer.WrittenSpan.ToArray(), typeof(SnapshotEnvelope)), snapshot);
            }
            system.Serialization.FindSerializerFor(new UserV1("a")).Identifier.Should().Be(9719);
            system.Serialization.FindSerializerFor(new UserV2("a")).Identifier.Should().Be(9720);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RejectUnknownMessagesAndMalformedRecords_When_ReadingNativeSnapshots")]
    public void Should_RejectUnknownMessagesAndMalformedRecords_When_ReadingNativeSnapshots()
    {
        var native = new NativePersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
        Action manifest = () => native.Manifest(new object());
        var buffer = new ArrayBufferWriter<byte>();
        Action write = () => native.Serialize(new object(), buffer);
        Action unknown = () => native.Deserialize(ReadOnlySequence<byte>.Empty, "unknown");
        Action truncated = () => native.Deserialize(new ReadOnlySequence<byte>(new byte[] { 0x12, 0x05, 0x01, 0x02 }), SnapshotManifest);
        manifest.Should().Throw<ArgumentException>();
        write.Should().Throw<ArgumentException>();
        buffer.WrittenCount.Should().Be(0);
        unknown.Should().Throw<SerializationException>();
        truncated.Should().Throw<InvalidProtocolBufferException>();
    }

    [Theory(DisplayName = "Should_RestoreTransportInformation_When_TheUserPayloadSucceedsOrThrows")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RestoreTransportInformation_When_TheUserPayloadSucceedsOrThrows(bool fail)
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("snapshot-transport", new TransportSerializer(system), ImmutableHashSet.Create(typeof(TransportPayload))))));
        var system = ActorSystem.Create($"snapshot-transport-{fail}", setup);
        InitializeLogger(system);
        try
        {
            var serializer = new NativePersistenceSnapshotSerializer((ExtendedActorSystem)system);
            var observer = (TransportSerializer)system.Serialization.FindSerializerFor(new TransportPayload(fail));
            var previous = AkkaSerialization.CurrentTransportInformation;
            try
            {
                foreach (var supplied in new Information?[] { null,
                             new(new Address("akka.tcp", "advertised-snapshot", "127.0.0.1", 2552), system) })
                {
                    AkkaSerialization.CurrentTransportInformation = supplied;
                    var buffer = new ArrayBufferWriter<byte>();
                    Action write = () => serializer.Serialize(new SnapshotEnvelope(new TransportPayload(fail)), buffer);
                    if (fail)
                    {
                        write.Should().Throw<InvalidOperationException>().WithMessage("payload failure");
                        buffer.WrittenCount.Should().Be(0);
                    }
                    else
                        write();
                    observer.Observed.Should().BeSameAs(supplied ?? ((ExtendedActorSystem)system).Provider.SerializationInformation);
                    AkkaSerialization.CurrentTransportInformation.Should().BeSameAs(supplied);
                }
            }
            finally
            {
                AkkaSerialization.CurrentTransportInformation = previous;
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static void AssertData(object actual, SnapshotEnvelope expected) =>
        actual.Should().BeOfType<SnapshotEnvelope>().Which.Data.Should().BeEquivalentTo(expected.Data);

    private static ReadOnlySequence<byte> Split(byte[] bytes, int offset)
    {
        var first = new Segment(bytes.AsMemory(0, offset));
        var last = first.Append(bytes.AsMemory(offset));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    public sealed class CustomLegacySerializer(ExtendedActorSystem system) : PersistenceSnapshotSerializer(system)
    {
        public override bool IncludeManifest => false;
        public override byte[] ToBinary(object obj) => new byte[] { 42 };
        public override object FromBinary(byte[] bytes, Type type) => new SnapshotEnvelope("custom-legacy");
    }

    private sealed record UserV1(string Value);
    private sealed record UserV2(string Value);
    private sealed record TransportPayload(bool Fail);

    private sealed class TransportSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public Information? Observed { get; private set; }
        public override int Identifier => 9721;
        public override string Manifest(object obj) => "transport";
        public override byte[] ToBinary(object obj)
        {
            Observed = AkkaSerialization.CurrentTransportInformation;
            if (((TransportPayload)obj).Fail)
                throw new InvalidOperationException("payload failure");
            return Array.Empty<byte>();
        }
        public override object FromBinary(byte[] bytes, string manifest) => new TransportPayload(false);
    }

    private sealed class UserV1Serializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 9719;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => new UserV1(Encoding.UTF8.GetString(bytes));
    }

    private sealed class UserV2Serializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9720;
        public override string Manifest(object obj) => "u2";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((UserV2)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new UserV2(Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
