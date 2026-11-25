//-----------------------------------------------------------------------
// <copyright file="ManifestBytesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Serialization;

public class ManifestBytesSpec : AkkaSpec
{
    public ManifestBytesSpec(ITestOutputHelper output) : base(output) { }

    [Theory(DisplayName = "Should_preserve_string_dispatch_and_transport_When_manifest_is_UTF8")]
    [InlineData("")]
    [InlineData("custom-Ω-😀")]
    [InlineData("System.String, System.Private.CoreLib")]
    public void Default_dispatch_is_equivalent(string manifest)
    {
        var bytes = Array.Empty<byte>();
        var utf8 = Encoding.UTF8.GetBytes(manifest);
        Check(new ReadOnlySequence<byte>(utf8), manifest);
        for (var split = 0; split <= utf8.Length; split++)
            Check(Split(utf8, split), manifest);

        // Invalid UTF-8 must use the same replacement fallback, including a split code point.
        var invalid = new byte[] { 0xE2, 0x82, 0x28 };
        Check(Split(invalid, 1), Encoding.UTF8.GetString(invalid));

        void Check(ReadOnlySequence<byte> raw, string expected)
        {
            var legacy = new Legacy((ExtendedActorSystem)Sys);
            var native = new Native((ExtendedActorSystem)Sys);
            Sys.Serialization.AddSerializer("manifest-bytes-legacy", legacy);
            Sys.Serialization.AddSerializer("manifest-bytes-native", native);
            foreach (var serializer in new Serializer[] { legacy, native })
            {
                Assert.Equal(expected, Sys.Serialization.Deserialize(new ReadOnlySequence<byte>(bytes), serializer.Identifier, raw));
                Assert.Equal(expected, serializer.FromBinary(bytes, expected));
            }
            Assert.Null(Akka.Serialization.Serialization.CurrentTransportInformation);
        }
    }

    [Fact(DisplayName = "Should_preserve_error_and_restore_transport_When_deserialization_fails")]
    public void Errors_preserve_behavior()
    {
        var raw = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("unknown-Ω"));
        var bytes = Array.Empty<byte>();
        var expected = Assert.Throws<SerializationException>(() => Sys.Serialization.Deserialize(bytes, -98765, "unknown-Ω"));
        Assert.Equal(expected.Message, Assert.Throws<SerializationException>(() => Sys.Serialization.Deserialize(new ReadOnlySequence<byte>(bytes), -98765, raw)).Message);
        var serializer = new Native((ExtendedActorSystem)Sys);
        Sys.Serialization.AddSerializer("manifest-bytes-failure", serializer);
        Assert.Throws<SerializationException>(() => Sys.Serialization.Deserialize(new ReadOnlySequence<byte>(bytes), serializer.Identifier, new ReadOnlySequence<byte>("throw"u8.ToArray())));
        Assert.Null(Akka.Serialization.Serialization.CurrentTransportInformation);
    }

    private sealed class Legacy(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => -901;
        public override string Manifest(object o) => "custom";
        public override byte[] ToBinary(object obj) => Array.Empty<byte>();
        public override object FromBinary(byte[] bytes, string manifest) => manifest;
    }

    private sealed class Native(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => -902;
        public override string Manifest(object obj) => "custom";
        public override int Serialize(object obj, IBufferWriter<byte> writer) => 0;
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (manifest == "throw")
                throw new SerializationException("test failure");
            return manifest;
        }
    }

    private static ReadOnlySequence<byte> Split(byte[] bytes, int split)
    {
        var first = new Segment(bytes.AsMemory(0, split));
        var last = first.Append(bytes.AsMemory(split));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
