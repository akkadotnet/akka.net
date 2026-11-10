// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class StreamRefProtocolV2Spec : V2PortSpec
{
    public StreamRefProtocolV2Spec(ITestOutputHelper output) : base(output, ActorMaterializer.DefaultConfig())
    {
        var cases = new List<V2PortCase>
        {
            new(new SequencedOnNext(0, "payload"), "A", "next-string"),
            new(new SequencedOnNext(long.MaxValue, ""), "A", "next-empty-string"),
            new(new SequencedOnNext(3, new byte[] { 0, 1, 127, 255 }), "A", "next-bytes"),
            new(new SequencedOnNext(4, 17), "A", "next-int"),
            new(new CumulativeDemand(1), "B", "demand"),
            new(new CumulativeDemand(long.MaxValue), "B", "demand-max"),
            new(new RemoteStreamFailure("failed \u2603"), "C", "failure"),
            new(new RemoteStreamFailure(""), "C", "failure-empty"),
            new(new RemoteStreamCompleted(0), "D", "completed"),
            new(new RemoteStreamCompleted(long.MaxValue), "D", "completed-max")
        };
        foreach (var (target, name) in new[] { (ActorRefs.Nobody, "handshake"), (ActorRefs.NoSender, "handshake-empty") })
        {
            cases.Add(new V2PortCase(new OnSubscribeHandshake(target), "G", name)
            {
                AssertEquivalent = (expected, actual) =>
                {
                    var legacy = CreateLegacy((ExtendedActorSystem)Sys);
                    var canonical = (OnSubscribeHandshake)legacy.FromBinary(legacy.ToBinary(expected), "G");
                    ((OnSubscribeHandshake)actual).TargetRef.Path.Should().Be(canonical.TargetRef.Path);
                }
            });
        }
        foreach (var (reference, manifest, name) in new (object, string, string)[]
        {
            (new SourceRefImpl<string>(ActorRefs.Nobody), "E", "source-string"),
            (new SourceRefImpl<int>(ActorRefs.Nobody), "E", "source-int"),
            (new SourceRefImpl<string>(ActorRefs.NoSender), "E", "source-empty"),
            (new SinkRefImpl<string>(ActorRefs.Nobody), "F", "sink-string"),
            (new SinkRefImpl<int>(ActorRefs.Nobody), "F", "sink-int"),
            (new SinkRefImpl<string>(ActorRefs.NoSender), "F", "sink-empty")
        })
        {
            cases.Add(new V2PortCase(reference, manifest, name)
            {
                RequiresDynamicTypeLoading = true,
                AssertEquivalent = (expected, actual) =>
                {
                    var legacy = CreateLegacy((ExtendedActorSystem)Sys);
                    var canonical = legacy.FromBinary(legacy.ToBinary(expected), manifest);
                    var expectedType = canonical is SourceRefImpl source ? source.EventType : ((SinkRefImpl)canonical).EventType;
                    var actualType = actual is SourceRefImpl readSource ? readSource.EventType : ((SinkRefImpl)actual).EventType;
                    var expectedRef = canonical is SourceRefImpl expectedSource ? expectedSource.InitialPartnerRef : ((SinkRefImpl)canonical).InitialPartnerRef;
                    var actualRef = actual is SourceRefImpl actualSource ? actualSource.InitialPartnerRef : ((SinkRefImpl)actual).InitialPartnerRef;
                    actualType.Should().Be(expectedType);
                    actualRef.Path.Should().Be(expectedRef.Path);
                }
            });
        }
        Cases = cases;
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new Akka.Streams.Serialization.StreamRefSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new StreamRefProtocolMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases { get; }

    [Fact(DisplayName = "Should_ExchangeBothProtocolFormats_When_OnlyOnePeerEnablesV2")]
    public async Task Should_ExchangeBothProtocolFormats_When_OnlyOnePeerEnablesV2()
    {
        await using var pair = MixedBindingPair.Create("stream-protocol-mixed",
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ActorMaterializer.DefaultConfig()),
            ActorMaterializer.DefaultConfig());
        AssertMixed(pair);
    }

    [Fact(DisplayName = "Should_PreserveTheWholeProtocolPin_When_V2IsEnabled")]
    public async Task Should_PreserveTheWholeProtocolPin_When_V2IsEnabled()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ActorMaterializer.DefaultConfig());
        await using var pair = MixedBindingPair.Create("stream-protocol-pinned", enabled,
            MixedBindingPair.BindTo("akka-stream-ref", typeof(IStreamRefsProtocol), typeof(SourceRefImpl), typeof(SinkRefImpl)).WithFallback(enabled));
        AssertMixed(pair);
    }

    private void AssertMixed(MixedBindingPair pair)
    {
        pair.AssertRoundTripsBothWays(Cases.Where(sample => sample.Manifest is not ("E" or "F" or "G")), 70, 30);
        foreach (var (writer, reader, id) in new[] { (pair.V2Node, pair.LegacyNode, 70), (pair.LegacyNode, pair.V2Node, 30) })
        {
            // Nobody's legacy path and NoSender canonically return the receiver's
            // deadLetters. Assert that receiving-system identity instead of the spec's system.
            foreach (var target in new[] { ActorRefs.Nobody, ActorRefs.NoSender })
            {
                var message = new OnSubscribeHandshake(target);
                var serializer = writer.Serialization.FindSerializerFor(message);
                serializer.Identifier.Should().Be(id);
                var received = (OnSubscribeHandshake)reader.Serialization.Deserialize(serializer.ToBinary(message), id, "G");
                received.TargetRef.Should().BeSameAs(reader.DeadLetters);
                foreach (var reference in new object[] { new SourceRefImpl<string>(target), new SinkRefImpl<string>(target) })
                {
                    var refWriter = writer.Serialization.FindSerializerFor(reference);
                    refWriter.Identifier.Should().Be(id);
                    var manifest = refWriter.Manifest(reference);
                    var restored = reader.Serialization.Deserialize(refWriter.ToBinary(reference), id, manifest);
                    var partner = restored is SourceRefImpl source ? source.InitialPartnerRef : ((SinkRefImpl)restored).InitialPartnerRef;
                    partner.Should().BeSameAs(reader.DeadLetters);
                }
            }
        }
    }

    [Theory(DisplayName = "Should_SelectTheMatchingWriterForBareRefs_When_TheGlobalSwitchChanges")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_SelectTheMatchingWriterForBareRefs_When_TheGlobalSwitchChanges(bool enabled)
    {
        var config = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}")
            .WithFallback(ActorMaterializer.DefaultConfig());
        var system = ActorSystem.Create("stream-bare-refs", config);
        InitializeLogger(system);
        try
        {
            foreach (var reference in new object[] { new SourceRefImpl<string>(system.DeadLetters), new SinkRefImpl<string>(system.DeadLetters) })
                system.Serialization.FindSerializerFor(reference).Identifier.Should().Be(enabled ? 70 : 30);
            system.Serialization.FindSerializerFor(new CumulativeDemand(1)).Identifier.Should().Be(enabled ? 70 : 30);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory(DisplayName = "Should_PreserveProviderSpecificRefs_When_TheTargetIsEmptyOrUnresolved")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_PreserveProviderSpecificRefs_When_TheTargetIsEmptyOrUnresolved(bool remote)
    {
        var config = remote ? ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.dot-netty.tcp { hostname = "127.0.0.1", port = 0 }
            """) : Config.Empty;
        var system = ActorSystem.Create($"stream-handshake-{remote}", config.WithFallback(ActorMaterializer.DefaultConfig()));
        InitializeLogger(system);
        try
        {
            var actor = system.ActorOf(Props.Empty, "target");
            var legacy = CreateLegacy((ExtendedActorSystem)system);
            var modern = CreateV2((ExtendedActorSystem)system);
            foreach (var target in new[] { ActorRefs.NoSender, ActorRefs.Nobody, system.DeadLetters, actor })
            {
                var message = new OnSubscribeHandshake(target);
                var expected = (OnSubscribeHandshake)legacy.FromBinary(legacy.ToBinary(message), "G");
                var actual = (OnSubscribeHandshake)modern.FromBinary(modern.ToBinary(message), "G");
                if (expected.TargetRef is null)
                {
                    actual.TargetRef.Should().BeNull();
                }
                else
                {
                    actual.TargetRef.Should().NotBeNull();
                    actual.TargetRef.GetType().Should().Be(expected.TargetRef.GetType());
                    actual.TargetRef.Path.ToSerializationFormat().Should().Be(expected.TargetRef.Path.ToSerializationFormat());
                    // Resolving an unknown serialized path can allocate an EmptyLocalActorRef on
                    // each read. Preserve identity only where the legacy reader returns an existing ref.
                    if (ReferenceEquals(expected.TargetRef, actor) || ReferenceEquals(expected.TargetRef, system.DeadLetters))
                        actual.TargetRef.Should().BeSameAs(expected.TargetRef);
                }
                foreach (var reference in new object[] { new SourceRefImpl<string>(target), new SinkRefImpl<int>(target) })
                {
                    var manifest = legacy.Manifest(reference);
                    var canonical = legacy.FromBinary(legacy.ToBinary(reference), manifest);
                    var restored = modern.FromBinary(modern.ToBinary(reference), manifest);
                    var canonicalRef = canonical is SourceRefImpl source ? source.InitialPartnerRef : ((SinkRefImpl)canonical).InitialPartnerRef;
                    var restoredRef = restored is SourceRefImpl restoredSource ? restoredSource.InitialPartnerRef : ((SinkRefImpl)restored).InitialPartnerRef;
                    restored.GetType().Should().Be(canonical.GetType());
                    if (canonicalRef is null)
                    {
                        restoredRef.Should().BeNull();
                    }
                    else
                    {
                        restoredRef.Should().NotBeNull();
                        restoredRef.GetType().Should().Be(canonicalRef.GetType());
                        restoredRef.Path.ToSerializationFormat().Should().Be(canonicalRef.Path.ToSerializationFormat());
                        if (ReferenceEquals(canonicalRef, actor) || ReferenceEquals(canonicalRef, system.DeadLetters))
                            restoredRef.Should().BeSameAs(canonicalRef);
                    }
                }
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_UseIndependentUserCodecs_When_TheProtocolWrapsV1AndV2Payloads")]
    public async Task Should_UseIndependentUserCodecs_When_TheProtocolWrapsV1AndV2Payloads()
    {
        var setup = ActorSystemSetup.Create(
            BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on")
                .WithFallback(ActorMaterializer.DefaultConfig())),
            SerializationSetup.Create(owner => ImmutableHashSet.Create(
                SerializerDetails.Create("stream-user-v1", new UserV1Serializer(owner), ImmutableHashSet.Create(typeof(UserV1))),
                SerializerDetails.Create("stream-user-v2", new UserV2Serializer(owner), ImmutableHashSet.Create(typeof(UserV2))))));
        var system = ActorSystem.Create("stream-user-codecs", setup);
        InitializeLogger(system);
        try
        {
            var legacy = CreateLegacy((ExtendedActorSystem)system);
            var modern = CreateV2((ExtendedActorSystem)system);
            foreach (var payload in new object[] { new UserV1("one"), new UserV2("two") })
            {
                var message = new SequencedOnNext(42, payload);
                system.Serialization.FindSerializerFor(message).Identifier.Should().Be(70);
                system.Serialization.FindSerializerFor(payload).Identifier.Should().Be(payload is UserV1 ? 70701 : 70702);
                foreach (var serializer in new[] { legacy, modern })
                {
                    var bytes = serializer.ToBinary(message);
                    var restored = (SequencedOnNext)system.Serialization.Deserialize(bytes, serializer.Identifier, "A");
                    restored.SeqNr.Should().Be(42);
                    restored.Payload.Should().BeOfType(payload.GetType()).And.Be(payload);
                }
                var buffer = new ArrayBufferWriter<byte>();
                modern.Serialize(message, buffer).Should().Be(buffer.WrittenCount);
                var direct = (SequencedOnNext)modern.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "A");
                direct.Payload.Should().BeOfType(payload.GetType()).And.Be(payload);
                direct.SeqNr.Should().Be(42);
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory(DisplayName = "Should_RejectBareRefsBeforeOutput_When_DynamicLoadingIsDisabledColdOrWarm")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_RejectBareRefsBeforeOutput_When_DynamicLoadingIsDisabledColdOrWarm(bool warm)
    {
        foreach (var reference in new object[] { new SourceRefImpl<string>(Sys.DeadLetters), new SinkRefImpl<int>(Sys.DeadLetters) })
        {
            var writer = CreateV2((ExtendedActorSystem)Sys);
            var bytes = writer.ToBinary(reference);
            var manifest = writer.Manifest(reference);
            var reader = warm ? writer : CreateV2((ExtendedActorSystem)Sys);
            if (warm)
                writer.FromBinary(bytes, manifest).GetType().Should().Be(reference.GetType());
            var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
            AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
            try
            {
                var codec = warm ? reader : CreateV2((ExtendedActorSystem)Sys);
                var registered = new Akka.Serialization.Serialization((ExtendedActorSystem)Sys);
                var output = new ArrayBufferWriter<byte>();
                Action[] unsupported =
                [
                    () => codec.Manifest(reference),
                    () => codec.SizeHint(reference),
                    () => codec.ToBinary(reference),
                    () => codec.Serialize(reference, output),
                    () => codec.FromBinary(bytes, manifest),
                    () => codec.Deserialize(new ReadOnlySequence<byte>(bytes), manifest),
                    () => registered.Deserialize(bytes, 70, manifest)
                ];
                foreach (var action in unsupported)
                    action.Should().Throw<SerializationException>().WithMessage("*Akka.DynamicTypeLoading*");
                output.WrittenCount.Should().Be(0);
            }
            finally
            {
                AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
            }
        }
    }

    [Fact(DisplayName = "Should_RejectUnknownTokens_When_TheStreamRefCodecIsUsedDirectly")]
    public void Should_RejectUnknownTokens_When_TheStreamRefCodecIsUsedDirectly()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Action unknown = () => serializer.FromBinary(new byte[] { 0x80 }, "unknown");
        unknown.Should().Throw<SerializationException>();
    }

    private sealed record UserV1(string Value);
    private sealed record UserV2(string Value);

    private sealed class UserV1Serializer(ExtendedActorSystem owner) : SerializerWithStringManifest(owner)
    {
        public override int Identifier => 70701;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => new UserV1(Encoding.UTF8.GetString(bytes));
    }

    private sealed class UserV2Serializer(ExtendedActorSystem owner) : SerializerV2(owner)
    {
        public override int Identifier => 70702;
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
