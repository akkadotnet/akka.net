// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class ArteryUtf8CountReuseSpec : AkkaSpec
{
    public ArteryUtf8CountReuseSpec(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(null, null, "manifest")]
    [InlineData("", "", "manifest")]
    [InlineData("sender", "recipient", "manifest")]
    [InlineData("送信-Ω", "受信-😀", "型-Ä")]
    [InlineData("a\0b", "r\0x", "m\0y")]
    public void Should_match_unchanged_span_encoder_for_absent_ascii_and_Unicode_literals(string? sender, string? recipient, string? manifest)
    {
        AssertMatchesSpan(sender, recipient, manifest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_preserve_native_empty_manifest_rejection_before_hint_rent_or_literal_validation(string? manifest)
    {
        var events = new List<string>();
        Install(events, manifest);
        var pool = new ObservedPool(events);
        Assert.Throws<SerializationException>(() => ArteryEnvelopeCodec.Encode(
            Sys.Serialization, 42, new string('x', 65536), null, new Payload(), pool));
        Assert.Equal(new[] { "manifest" }, events);
        Assert.Equal(0, pool.Rents);
        Assert.Equal(0, pool.Returns);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Should_preserve_builtin_byte_array_empty_manifest_contract(string? sender, string? recipient)
    {
        var payload = new byte[] { 0x42 };
        var serializer = Sys.Serialization.FindSerializerV2For(payload);
        Assert.Equal("", Akka.Serialization.Serialization.ManifestFor(serializer, payload));
        var pool = new ObservedPool(new List<string>());
        using (var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, sender, recipient, payload, pool))
        {
            var expected = new byte[ArteryEnvelopeCodec.MaxEncodedSize(sender, recipient, "", 1)];
            var length = ArteryEnvelopeCodec.Encode(expected, 42, serializer.Identifier, sender, recipient, "", payload);
            Assert.Equal(expected.AsSpan(0, length).ToArray(), writer.WrittenMemory.ToArray());
            Assert.Equal(length, writer.WrittenCount);
        }
        Assert.Equal(1, pool.Returns);
        Assert.False(pool.ClearRequested);
    }

    [Fact]
    public void Should_preserve_default_UTF8_replacement_for_unpaired_surrogates()
    {
        AssertMatchesSpan("sender-\uD800", "recipient-\uDC00", "manifest-\uD800x\uDC00");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Should_preserve_maximum_65535_byte_literal_at_each_position(int position)
    {
        var value = new string('é', 32767) + "a";
        Assert.Equal(65535, Encoding.UTF8.GetByteCount(value));
        var values = new[] { "sender", "recipient", "manifest" };
        values[position] = value;
        AssertMatchesSpan(values[0], values[1], values[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Should_validate_over_limit_at_original_write_position_and_return_rent(int position)
    {
        var events = new List<string>();
        var values = new[] { "s", "r", "m" };
        values[position] = new string('漢', 21845) + "a";
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(values[position]));
        var serializer = Install(events, values[2]);
        var pool = new ObservedPool(events);
        var exception = Assert.Throws<ArteryEnvelopeException>(() =>
            ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, values[0], values[1], new Payload(), pool));
        Assert.Contains("65535", exception.Message);
        Assert.Contains("65536", exception.Message);
        Assert.Equal(new[] { "manifest", "hint", "rent", "return" }, events);
        Assert.Equal(1, pool.Rents);
        Assert.Equal(1, pool.Returns);
        Assert.False(pool.ClearRequested);
        Assert.Equal(1, serializer.ManifestCalls);
        var returned = Assert.IsType<byte[]>(pool.ReturnedBytes);
        if (position == 0) Assert.Equal(0xA5, returned[36]);
        else
        {
            // Earlier valid literals must already have been written when the later one fails.
            Assert.Equal(1, returned[36]);
            Assert.Equal(0, returned[37]);
            Assert.Equal((byte)'s', returned[38]);
            if (position == 2)
            {
                Assert.Equal(1, returned[39]);
                Assert.Equal(0, returned[40]);
                Assert.Equal((byte)'r', returned[41]);
            }
        }
    }

    [Fact]
    public void Should_keep_SizeHint_failure_before_literal_validation_or_rent()
    {
        var events = new List<string>();
        var serializer = Install(events, "manifest");
        var expected = new InvalidOperationException("size-hint-failure");
        serializer.OnHint = () => throw expected;
        var pool = new ObservedPool(events);
        var actual = Assert.Throws<InvalidOperationException>(() => ArteryEnvelopeCodec.Encode(
            Sys.Serialization, 42, new string('x', 65536), "recipient", new Payload(), pool));
        Assert.Same(expected, actual);
        Assert.Equal(new[] { "manifest", "hint" }, events);
        Assert.Equal(0, pool.Rents);
        Assert.Equal(0, pool.Returns);
    }

    [Fact]
    public void Should_keep_rent_failure_before_literal_validation()
    {
        var events = new List<string>();
        Install(events, "manifest");
        var expected = new InvalidOperationException("rent-failure");
        var pool = new ObservedPool(events) { RentFailure = expected };
        var actual = Assert.Throws<InvalidOperationException>(() => ArteryEnvelopeCodec.Encode(
            Sys.Serialization, 42, new string('x', 65536), "recipient", new Payload(), pool));
        Assert.Same(expected, actual);
        Assert.Equal(new[] { "manifest", "hint", "rent" }, events);
        Assert.Equal(0, pool.Returns);
    }

    [Fact]
    public void Should_use_captured_string_snapshots_when_serializer_changes_later_getter_values()
    {
        var events = new List<string>();
        var serializer = Install(events, "old-manifest-Ω");
        var sender = "old-sender-送";
        var recipient = "old-recipient-😀";
        serializer.OnHint = () =>
        {
            sender = "changed-sender";
            recipient = "changed-recipient";
            serializer.ManifestValue = "changed-manifest";
        };
        using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, sender, recipient, new Payload());
        var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory.Slice(4)));
        Assert.True(decoded.TryGetSenderPath(out var actualSender));
        Assert.True(decoded.TryGetRecipientPath(out var actualRecipient));
        Assert.True(decoded.TryGetManifest(out var actualManifest));
        Assert.Equal("old-sender-送", actualSender);
        Assert.Equal("old-recipient-😀", actualRecipient);
        Assert.Equal("old-manifest-Ω", actualManifest);
        Assert.Equal(1, serializer.ManifestCalls);
        Assert.Equal(new[] { "manifest", "hint", "serialize" }, events);
    }

    private void AssertMatchesSpan(string? sender, string? recipient, string? manifest)
    {
        var events = new List<string>();
        var serializer = Install(events, manifest);
        var pool = new ObservedPool(events);
        using (var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, sender, recipient, new Payload(), pool))
        {
            var expected = new byte[ArteryEnvelopeCodec.MaxEncodedSize(sender, recipient, manifest ?? "", 1)];
            var length = ArteryEnvelopeCodec.Encode(expected, 42, serializer.Identifier, sender, recipient, manifest ?? "", new byte[] { 0x42 });
            Assert.Equal(expected.AsSpan(0, length).ToArray(), writer.WrittenMemory.ToArray());
            Assert.Equal(length, writer.WrittenCount);
            Assert.Equal(new[] { "manifest", "hint", "rent", "serialize" }, events);
            Assert.Equal(1, serializer.ManifestCalls);
        }
        Assert.Equal(1, pool.Returns);
        Assert.False(pool.ClearRequested);
    }

    private ObservedSerializer Install(List<string> events, string? manifest)
    {
        var serializer = new ObservedSerializer((ExtendedActorSystem)Sys, events, manifest);
        Sys.Serialization.AddSerializer("utf8-count-reuse", serializer);
        Sys.Serialization.AddSerializationMap(typeof(Payload), serializer);
        return serializer;
    }
    private sealed class Payload { }
    private sealed class ObservedSerializer(ExtendedActorSystem system, List<string> events, string? manifest) : SerializerV2(system)
    {
        public override int Identifier => 987655;
        public string? ManifestValue { get; set; } = manifest;
        public Action? OnHint { get; set; }
        public int ManifestCalls { get; private set; }
        public override string Manifest(object obj) { events.Add("manifest"); ManifestCalls++; return ManifestValue!; }
        public override int SizeHint(object obj) { events.Add("hint"); OnHint?.Invoke(); return 1; }
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            events.Add("serialize");
            writer.GetSpan(1)[0] = 0x42;
            writer.Advance(1);
            return 1;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new Payload();
    }
    private sealed class ObservedPool(List<string> events) : ArrayPool<byte>
    {
        public int Rents { get; private set; }
        public int Returns { get; private set; }
        public bool ClearRequested { get; private set; }
        public byte[]? ReturnedBytes { get; private set; }
        public Exception? RentFailure { get; init; }
        public override byte[] Rent(int minimumLength)
        {
            events.Add("rent");
            Rents++;
            if (RentFailure != null) throw RentFailure;
            var array = new byte[minimumLength];
            Array.Fill(array, (byte)0xA5);
            return array;
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            events.Add("return");
            Returns++;
            ClearRequested |= clearArray;
            ReturnedBytes = (byte[])array.Clone();
        }
    }
}
