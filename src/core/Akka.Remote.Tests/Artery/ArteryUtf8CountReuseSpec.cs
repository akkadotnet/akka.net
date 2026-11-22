// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable

using System.Buffers;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class ArteryUtf8CountReuseSpec : AkkaSpec
{
    public ArteryUtf8CountReuseSpec(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Should_use_manifest_captured_once_before_size_hint_changes_serializer_state()
    {
        var events = new List<string>();
        var serializer = new ObservedSerializer((ExtendedActorSystem)Sys, events);
        Sys.Serialization.AddSerializer("utf8-count-reuse", serializer);
        Sys.Serialization.AddSerializationMap(typeof(Payload), serializer);
        using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, null, new Payload());
        var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory.Slice(4)));
        Assert.True(decoded.TryGetManifest(out var manifest));
        Assert.Equal("old-manifest", manifest);
        Assert.Equal("changed-manifest", serializer.ManifestValue);
        Assert.Equal(1, serializer.ManifestCalls);
        Assert.Equal(new[] { "manifest", "hint", "serialize" }, events);
    }

    private sealed class Payload { }

    private sealed class ObservedSerializer(ExtendedActorSystem system, List<string> events) : SerializerV2(system)
    {
        public override int Identifier => 987655;
        public string ManifestValue { get; private set; } = "old-manifest";
        public int ManifestCalls { get; private set; }
        public override string Manifest(object obj)
        {
            events.Add("manifest");
            ManifestCalls++;
            return ManifestValue;
        }
        public override int SizeHint(object obj)
        {
            events.Add("hint");
            ManifestValue = "changed-manifest";
            return 1;
        }
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            events.Add("serialize");
            writer.GetSpan(1)[0] = 0x42;
            writer.Advance(1);
            return 1;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new Payload();
    }
}
