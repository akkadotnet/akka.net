// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Serialization;
using Akka.Remote.Tests.Serialization;
using Akka.TestKit;
using Google.Protobuf;
using MessagePack;
using Xunit;
using Proto = Akka.Remote.Serialization.Proto.Msg;

namespace Akka.Remote.Tests.Artery;

public sealed class MessageContainerLaneKeySpec : AkkaSpec
{
    public MessageContainerLaneKeySpec(ITestOutputHelper output)
        : base(output, MessageContainerMessagePackSerializerSpec.PortConfig)
    {
    }

    [Fact(DisplayName = "Should_ChooseTheSameSelectionLaneKey_When_EitherWireFormatIsSegmented")]
    public void Should_ChooseTheSameSelectionLaneKey_When_EitherWireFormatIsSegmented()
    {
        var legacy = new MessageContainerSerializer((ExtendedActorSystem)Sys);
        var modern = new MessageContainerMessagePackSerializer((ExtendedActorSystem)Sys);
        foreach (var sample in MessageContainerMessagePackSerializerSpec.Corpus)
        {
            var selection = Assert.IsType<ActorSelectionMessage>(sample.Message);
            var expected = selection.Elements.Length == 0 ? null : string.Concat(selection.Elements.Select(element =>
                "/" + (element is SelectParent ? "" : element.ToString())));
            var legacyBytes = legacy.ToBinary(selection);
            var modernBytes = modern.ToBinary(selection);
            Assert.Equal(expected, ArteryInboundProcessingStage.BuildSelectionLaneKey(new ReadOnlySequence<byte>(legacyBytes), 6));
            Assert.Equal(expected, ArteryInboundProcessingStage.BuildSelectionLaneKey(SplitEveryByte(legacyBytes), 6));
            Assert.Equal(expected, ArteryInboundProcessingStage.BuildSelectionLaneKey(new ReadOnlySequence<byte>(modernBytes), 46));
            Assert.Equal(expected, ArteryInboundProcessingStage.BuildSelectionLaneKey(SplitEveryByte(modernBytes), 46));
        }
    }

    [Fact(DisplayName = "Should_SkipAnUndecodablePayload_When_ArteryReadsOnlySelectionMetadata")]
    public void Should_SkipAnUndecodablePayload_When_ArteryReadsOnlySelectionMetadata()
    {
        var legacy = new Proto.SelectionEnvelope
        {
            Payload = new Proto.Payload { SerializerId = 999999, MessageManifest = ByteString.CopyFromUtf8("opaque"), Message = ByteString.CopyFrom(new byte[] { 0xc1 }) },
            Pattern =
            {
                new Proto.Selection { Type = Proto.Selection.Types.PatternType.ChildName, Matcher = "user" },
                new Proto.Selection { Type = Proto.Selection.Types.PatternType.ChildName, Matcher = "target" }
            }
        }.ToByteArray();
        var modern = OpaqueSelection(0, "target");
        Assert.Equal("/user/target", ArteryInboundProcessingStage.BuildSelectionLaneKey(SplitEveryByte(legacy), 6));
        Assert.Equal("/user/target", ArteryInboundProcessingStage.BuildSelectionLaneKey(SplitEveryByte(modern), 46));

        // These bytes cannot deserialize their application payload. Successful path extraction
        // therefore proves the routing parser never invokes that payload's serializer.
        Assert.Throws<SerializationException>(() => new MessageContainerSerializer((ExtendedActorSystem)Sys).FromBinary(legacy, typeof(ActorSelectionMessage)));
        var error = Assert.Throws<SerializationException>(() =>
            new MessageContainerMessagePackSerializer((ExtendedActorSystem)Sys).FromBinary(modern, "a"));
        Assert.Contains("/user/target", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Theory(DisplayName = "Should_FallBackToTheAnchor_When_SelectionMetadataIsInvalid")]
    [InlineData(-1, "target")]
    [InlineData(99, "target")]
    [InlineData(0, null)]
    [InlineData(1, null)]
    public void Should_FallBackToTheAnchor_When_SelectionMetadataIsInvalid(int kind, string? matcher)
    {
        var bytes = OpaqueSelection(kind, matcher);
        Assert.Null(ArteryInboundProcessingStage.BuildSelectionLaneKey(SplitEveryByte(bytes), 46));
    }

    [Fact(DisplayName = "Should_FallBackToTheAnchor_When_TheOuterWrapperIsEmptyOrTruncated")]
    public void Should_FallBackToTheAnchor_When_TheOuterWrapperIsEmptyOrTruncated()
    {
        Assert.Null(ArteryInboundProcessingStage.BuildSelectionLaneKey(ReadOnlySequence<byte>.Empty, 6));
        Assert.Null(ArteryInboundProcessingStage.BuildSelectionLaneKey(ReadOnlySequence<byte>.Empty, 46));
        var bytes = OpaqueSelection(0, "target");
        for (var length = 0; length < bytes.Length; length++)
            Assert.Null(ArteryInboundProcessingStage.BuildSelectionLaneKey(new ReadOnlySequence<byte>(bytes.AsMemory(0, length)), 46));
    }

    private static byte[] OpaqueSelection(int kind, string? matcher)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(4);
        writer.Write(1);
        writer.WriteMapHeader(3);
        writer.Write(1);
        writer.Write(999999);
        writer.Write(2);
        writer.Write("opaque");
        writer.Write(3);
        writer.WriteBinHeader(1);
        writer.WriteRaw(new byte[] { 0xc1 });
        writer.Write(99);
        writer.WriteArrayHeader(1);
        writer.Write("future field");
        writer.Write(2);
        writer.WriteArrayHeader(2);
        WriteElement(ref writer, 0, "user");
        WriteElement(ref writer, kind, matcher);
        writer.Write(3);
        writer.Write(false);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteElement(ref MessagePackWriter writer, int kind, string? matcher)
    {
        writer.WriteMapHeader(2);
        writer.Write(1);
        writer.Write(kind);
        writer.Write(2);
        writer.Write(matcher);
    }

    private static ReadOnlySequence<byte> SplitEveryByte(byte[] bytes)
    {
        if (bytes.Length == 0)
            return ReadOnlySequence<byte>.Empty;
        var first = new Segment(bytes.AsMemory(0, 1));
        var last = first;
        for (var index = 1; index < bytes.Length; index++)
            last = last.Append(bytes.AsMemory(index, 1));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> bytes)
        {
            var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
