//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageNativeSerializerSpec.cs" company="Akka.NET Project">
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
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Google.Protobuf;
using VerifyXunit;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Persistence.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class PersistenceMessageNativeSerializerSpec : AkkaSpec
{
    private const string DynamicSwitch = "Akka.DynamicTypeLoading";

    public PersistenceMessageNativeSerializerSpec(ITestOutputHelper output) : base(output)
    {
    }

    // Original bytes captured before implementation from serializer git blob
    // d87af7aaac58d405302174cab9ab64438d682c70, upstream dev 2d6a06797.
    internal static IReadOnlyList<(string Name, object Message)> Corpus { get; } = new (string, object)[]
    {
        ("persistent", new Persistent("payload", 17, "pid", "adapter", true, ActorRefs.NoSender, "writer", 1234567890123456)),
        ("persistent-empty", new Persistent(Array.Empty<byte>())),
        ("persistent-null-negative", new Persistent(null, -1, "negative", timestamp: -1)),
        ("atomic-single", new AtomicWrite(new Persistent("one", 1, "pid", writerGuid: "writer", timestamp: 1000))),
        ("atomic-pair", new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
            new Persistent("one", 1, "pid", writerGuid: "writer", timestamp: 1000),
            new Persistent("two", 2, "pid", writerGuid: "writer", timestamp: 2000)))),
        ("delivery-empty", new AtLeastOnceDeliverySnapshot(0, Array.Empty<UnconfirmedDelivery>())),
        ("delivery", new AtLeastOnceDeliverySnapshot(long.MaxValue, new[]
        {
            new UnconfirmedDelivery(23, ActorPath.Parse("akka.tcp://delivery-fixture@127.0.0.1:2552/user/service"), "delivery")
        })),
        ("state-change", new PersistentFSM.StateChangeEvent("state", TimeSpan.FromTicks(1234567))),
        ("state-no-timeout", new PersistentFSM.StateChangeEvent("state", null)),
        ("state-zero-timeout", new PersistentFSM.StateChangeEvent("state", TimeSpan.Zero)),
        ("state-negative-timeout", new PersistentFSM.StateChangeEvent("state", TimeSpan.FromMilliseconds(-1))),
        ("fsm-snapshot", new PersistentFSM.PersistentFSMSnapshot<string>("state", "data", TimeSpan.FromTicks(1234567))),
        ("fsm-snapshot-no-timeout", new PersistentFSM.PersistentFSMSnapshot<string>("state", "data", null))
    };

    [Fact(DisplayName = "Should_PreserveHistoricalFixedAndGenericBytes_When_TheJitCodecIsAvailable")]
    public async Task Should_PreserveHistoricalFixedAndGenericBytes_When_TheJitCodecIsAvailable()
    {
        var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in Corpus)
        {
            var manifest = legacy.Manifest(sample.Message);
            native.Identifier.Should().Be(7);
            native.Manifest(sample.Message).Should().Be(manifest);
            var bytes = legacy.ToBinary(sample.Message);
            native.ToBinary(sample.Message).Should().Equal(bytes);
            var buffer = new ArrayBufferWriter<byte>();
            native.Serialize(sample.Message, buffer).Should().Be(bytes.Length);
            buffer.WrittenSpan.ToArray().Should().Equal(bytes);
            var expected = legacy.FromBinary(bytes, manifest);
            native.Deserialize(new ReadOnlySequence<byte>(bytes), manifest).Should().BeEquivalentTo(expected, o => o.RespectingRuntimeTypes().ComparingByMembers<Persistent>().ComparingByMembers<AtomicWrite>());
            var dump = HexDumpFormatter.Format(sample.Name, HexDumpFormatter.FriendlyTypeName(sample.Message.GetType()), manifest, 7, bytes);
            await Verifier.Verify(dump).UseDirectory("Snapshots")
                .UseFileName($"PersistenceMessageNativeSerializerSpec.{sample.Name}.legacy");
        }
    }

    [Fact(DisplayName = "Should_ReadAtEveryBoundary_When_HistoricalRecordsAreSegmented")]
    public void Should_ReadAtEveryBoundary_When_HistoricalRecordsAreSegmented()
    {
        var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in Corpus)
        {
            var manifest = legacy.Manifest(sample.Message);
            var bytes = legacy.ToBinary(sample.Message);
            var expected = legacy.FromBinary(bytes, manifest);
            for (var split = 0; split <= bytes.Length; split++)
                native.Deserialize(Split(bytes, split), manifest).Should().BeEquivalentTo(expected, o => o.RespectingRuntimeTypes().ComparingByMembers<Persistent>().ComparingByMembers<AtomicWrite>());
        }
    }

    [Theory(DisplayName = "Should_ResolveFixedShapesAndOldSerializerNames_When_DynamicTypeLoadingIsDisabled")]
    [InlineData("default")]
    [InlineData("stripped")]
    [InlineData("versioned")]
    public async Task Should_ResolveFixedShapesAndOldSerializerNames_When_DynamicTypeLoadingIsDisabled(string spelling)
    {
        var oldName = spelling == "versioned" ? typeof(PersistenceMessageSerializer).AssemblyQualifiedName
            : $"{typeof(PersistenceMessageSerializer).FullName}, Akka.Persistence";
        var config = spelling == "default" ? Config.Empty
            : ConfigurationFactory.ParseString($"akka.actor.serializers.akka-persistence-message = \"{oldName}\"");
        var system = ActorSystem.Create($"persistent-native-{spelling}", config);
        InitializeLogger(system);
        try
        {
            var hadSwitch = AppContext.TryGetSwitch(DynamicSwitch, out var previous);
            AppContext.SetSwitch(DynamicSwitch, false);
            try
            {
                var serialization = new AkkaSerialization((ExtendedActorSystem)system);
                var writer = serialization.FindSerializerForType(typeof(Persistent));
                writer.Should().BeOfType<NativePersistenceMessageSerializer>();
                serialization.GetSerializerById(7).Should().BeSameAs(writer);
                var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)system);
                foreach (var sample in FixedCases())
                {
                    var bytes = legacy.ToBinary(sample.Message);
                    var manifest = legacy.Manifest(sample.Message);
                    var expected = legacy.FromBinary(bytes, manifest);
                    writer.ToBinary(sample.Message).Should().Equal(bytes);
                    serialization.Deserialize(new ReadOnlySequence<byte>(bytes), 7, manifest).Should().BeEquivalentTo(expected, o => o.RespectingRuntimeTypes().ComparingByMembers<Persistent>().ComparingByMembers<AtomicWrite>());
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

    [Fact(DisplayName = "Should_RejectGenericFsmBeforeWriting_When_DynamicLoadingIsDisabledEvenAfterJitWarmup")]
    public void Should_RejectGenericFsmBeforeWriting_When_DynamicLoadingIsDisabledEvenAfterJitWarmup()
    {
        var sample = Corpus.First(c => c.Message.GetType().IsGenericType).Message;
        var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var bytes = legacy.ToBinary(sample);
        var manifest = legacy.Manifest(sample);
        legacy.FromBinary(bytes, manifest).Should().BeOfType(sample.GetType());
        var hadSwitch = AppContext.TryGetSwitch(DynamicSwitch, out var previous);
        AppContext.SetSwitch(DynamicSwitch, false);
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            Action write = () => native.Serialize(sample, buffer);
            Action array = () => native.ToBinary(sample);
            Action name = () => native.Manifest(sample);
            Action read = () => native.Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
            write.Should().Throw<SerializationException>().WithMessage("*Akka.DynamicTypeLoading*");
            array.Should().Throw<SerializationException>();
            name.Should().Throw<SerializationException>();
            read.Should().Throw<SerializationException>().WithMessage("*PersistentFSMSnapshot*");
            buffer.WrittenCount.Should().Be(0);
        }
        finally
        {
            AppContext.SetSwitch(DynamicSwitch, !hadSwitch || previous);
        }
    }

    [Fact(DisplayName = "Should_PreserveEmptySenderAndTimeoutSemantics_When_NativeReadsTheLegacyFormat")]
    public void Should_PreserveEmptySenderAndTimeoutSemantics_When_NativeReadsTheLegacyFormat()
    {
        var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var persistent = new Persistent("data", 1, "pid", timestamp: -1);
        var bytes = legacy.ToBinary(persistent);
        foreach (var manifest in new[] { string.Empty, typeof(Persistent).TypeQualifiedName(), typeof(IPersistentRepresentation).TypeQualifiedName() })
        {
            var decoded = (Persistent)native.Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
            decoded.Sender.Should().Be(Sys.DeadLetters);
            decoded.Timestamp.Should().Be(0);
        }
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) })
        {
            var change = new PersistentFSM.StateChangeEvent("state", timeout);
            ((PersistentFSM.StateChangeEvent)native.FromBinary(legacy.ToBinary(change), legacy.Manifest(change))).Timeout.Should().BeNull();
        }
        var fractional = new PersistentFSM.StateChangeEvent("state", TimeSpan.FromTicks(1234567));
        ((PersistentFSM.StateChangeEvent)native.FromBinary(legacy.ToBinary(fractional), legacy.Manifest(fractional)))
            .Timeout.Should().Be(TimeSpan.FromMilliseconds(123));
    }

    [Fact(DisplayName = "Should_PreserveSenderEncodingOrder_When_ThePayloadAddsTransportInformation")]
    public async Task Should_PreserveSenderEncodingOrder_When_ThePayloadAddsTransportInformation()
    {
        var config = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """);
        var system = ActorSystem.Create("persistent-sender-order", config);
        InitializeLogger(system);
        try
        {
            var probe = CreateTestProbe(system);
            var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)system);
            var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)system);
            var original = AkkaSerialization.CurrentTransportInformation;
            try
            {
                foreach (var supplied in new Information?[] { null,
                             new(new Address("akka.tcp", "advertised", "127.0.0.1", 2552), system) })
                {
                    AkkaSerialization.CurrentTransportInformation = supplied;
                    var expectedSender = AkkaSerialization.SerializedActorPath(probe.Ref);
                    var message = new Persistent("payload", 1, "pid", sender: probe.Ref);
                    var bytes = native.ToBinary(message);
                    bytes.Should().Equal(legacy.ToBinary(message));
                    Akka.Persistence.Serialization.Proto.Msg.PersistentMessage.Parser.ParseFrom(bytes).Sender.Should().Be(expectedSender);
                    AkkaSerialization.CurrentTransportInformation.Should().BeSameAs(supplied);
                }
            }
            finally
            {
                AkkaSerialization.CurrentTransportInformation = original;
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_PreserveRegisteredUserCodecs_When_NativeEnvelopesContainV1AndV2Payloads")]
    public async Task Should_PreserveRegisteredUserCodecs_When_NativeEnvelopesContainV1AndV2Payloads()
    {
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            SerializerDetails.Create("event-user-v1", new UserV1Serializer(system), ImmutableHashSet.Create(typeof(UserV1))),
            SerializerDetails.Create("event-user-v2", new UserV2Serializer(system), ImmutableHashSet.Create(typeof(UserV2))))));
        var system = ActorSystem.Create("persistent-user-codecs", setup);
        InitializeLogger(system);
        try
        {
            var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)system);
            var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)system);
            foreach (var payload in new object[] { new UserV1("v1"), new UserV2("v2") })
            {
                var message = new Persistent(payload, 1, "pid", timestamp: 123456);
                var bytes = legacy.ToBinary(message);
                var buffer = new ArrayBufferWriter<byte>();
                native.Serialize(message, buffer).Should().Be(bytes.Length);
                buffer.WrittenSpan.ToArray().Should().Equal(bytes);
                ((Persistent)native.Deserialize(new ReadOnlySequence<byte>(bytes), legacy.Manifest(message))).Payload.Should().Be(payload);
            }
            system.Serialization.FindSerializerFor(new UserV1("x")).Identifier.Should().Be(9831);
            system.Serialization.FindSerializerFor(new UserV2("x")).Identifier.Should().Be(9832);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RejectUnknownMessagesAndTruncatedRecords_When_UsingNativeEnvelopeBuffers")]
    public void Should_RejectUnknownMessagesAndTruncatedRecords_When_UsingNativeEnvelopeBuffers()
    {
        var native = new NativePersistenceMessageSerializer((ExtendedActorSystem)Sys);
        var buffer = new ArrayBufferWriter<byte>();
        Action write = () => native.Serialize(new object(), buffer);
        Action name = () => native.Manifest(new object());
        Action truncated = () => native.Deserialize(new ReadOnlySequence<byte>(new byte[] { 0x0a, 0x05, 0x01 }), typeof(Persistent).TypeQualifiedName());
        write.Should().Throw<ArgumentException>();
        name.Should().Throw<ArgumentException>();
        truncated.Should().Throw<InvalidProtocolBufferException>();
        buffer.WrittenCount.Should().Be(0);
    }

    internal static IEnumerable<(string Name, object Message)> FixedCases() => Corpus.Where(c => !c.Message.GetType().IsGenericType);

    private sealed record UserV1(string Value);
    private sealed record UserV2(string Value);

    private sealed class UserV1Serializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 9831;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => new UserV1(Encoding.UTF8.GetString(bytes));
    }

    private sealed class UserV2Serializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9832;
        public override string Manifest(object obj) => "u2";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((UserV2)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new UserV2(Encoding.UTF8.GetString(bytes.ToArray()));
    }

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
}
