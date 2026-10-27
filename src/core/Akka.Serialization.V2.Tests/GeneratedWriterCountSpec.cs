//-----------------------------------------------------------------------
// <copyright file="GeneratedWriterCountSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GeneratedWriterCountSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private WriterCountSerializer _plain = null!;
    private WriterCountFormattedSerializer _formatted = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("generated-writer-count-spec");
        _plain = new WriterCountSerializer((ExtendedActorSystem)_system);
        _formatted = new WriterCountFormattedSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _system.Terminate();

    [Theory(DisplayName = "Should_ExcludeExistingPrefixAndPreviousMessages_When_SerializingRepeatedly")]
    [InlineData(WriterKind.Pooled)]
    [InlineData(WriterKind.Array)]
    [InlineData(WriterKind.Opaque)]
    public void Should_exclude_existing_prefix_and_previous_messages_when_serializing_repeatedly(WriterKind kind)
    {
        using var destination = new WriterCase(kind);
        byte[] prefix = { 0xde, 0xad, 0xbe, 0xef };
        Append(destination.Writer, prefix);

        _plain.Serialize(new WriterCountMessage(new byte[] { 7, 9 }), destination.Writer).Should().Be(6);
        _plain.Serialize(new WriterCountMessage(new byte[] { 13 }), destination.Writer).Should().Be(5);

        destination.WrittenMemory.ToArray().Should().Equal(
            0xde, 0xad, 0xbe, 0xef, 0x81, 0x01, 0xc4, 0x02, 7, 9, 0x81, 0x01, 0xc4, 0x01, 13);
    }

    [Theory(DisplayName = "Should_CountEveryCommittedSegment_When_GeneratedPayloadOutgrowsInitialBuffer")]
    [InlineData(WriterKind.Pooled)]
    [InlineData(WriterKind.Array)]
    [InlineData(WriterKind.Opaque)]
    public void Should_count_every_committed_segment_when_generated_payload_outgrows_initial_buffer(WriterKind kind)
    {
        using var destination = new WriterCase(kind);
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        Append(destination.Writer, new byte[] { 0xaa });

        _plain.Serialize(new WriterCountMessage(payload), destination.Writer).Should().Be(1029);

        // map(field 1 -> bin16(1024 bytes)), independently specified rather than generated twice.
        destination.WrittenMemory.ToArray().Should().Equal(
            new byte[] { 0xaa, 0x81, 0x01, 0xc5, 0x04, 0x00 }.Concat(payload));
        if (destination.Writer is OpaqueWriter opaque)
            opaque.AdvanceCounts.Count(count => count > 0).Should().BeGreaterThan(2,
                "the prefix and generated payload must exercise more than one payload commit");
    }

    [Theory(DisplayName = "Should_IncludeExplicitFormatterFlushAndFinalCommit_When_ReturningWrittenCount")]
    [InlineData(WriterKind.Pooled)]
    [InlineData(WriterKind.Array)]
    [InlineData(WriterKind.Opaque)]
    public void Should_include_explicit_formatter_flush_and_final_commit_when_returning_written_count(WriterKind kind)
    {
        using var destination = new WriterCase(kind);
        Append(destination.Writer, new byte[] { 0xaa });
        var value = new WriterCountValue(7, 9)
        {
            AfterFlush = () => destination.WrittenMemory.ToArray().Should().Equal(0xaa, 0x81, 0x01, 0x92, 7)
        };

        _formatted.Serialize(new WriterCountFormattedMessage(value), destination.Writer).Should().Be(5);

        value.WriteCalls.Should().Be(1);
        destination.WrittenMemory.ToArray().Should().Equal(0xaa, 0x81, 0x01, 0x92, 7, 9);
    }

    [Theory(DisplayName = "Should_LeaveBufferedSuffixUncommitted_When_FormatterThrowsAfterFlush")]
    [InlineData(WriterKind.Pooled)]
    [InlineData(WriterKind.Array)]
    [InlineData(WriterKind.Opaque)]
    public void Should_leave_buffered_suffix_uncommitted_when_formatter_throws_after_flush(WriterKind kind)
    {
        using var destination = new WriterCase(kind);
        Append(destination.Writer, new byte[] { 0xaa });
        var failure = new InvalidOperationException("formatter failure after partial commit");
        var value = new WriterCountValue(7, 9) { Failure = failure };

        Action serialize = () => _formatted.Serialize(new WriterCountFormattedMessage(value), destination.Writer);

        serialize.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        value.WriteCalls.Should().Be(1);
        destination.WrittenMemory.ToArray().Should().Equal(0xaa, 0x81, 0x01, 0x92, 7);
    }

    [Theory(DisplayName = "Should_ExcludeAliasAdvances_When_FormatterWritesThroughAnotherDestinationReference")]
    [InlineData(WriterKind.Pooled)]
    [InlineData(WriterKind.Array)]
    [InlineData(WriterKind.Opaque)]
    public void Should_exclude_alias_advances_when_formatter_writes_through_another_destination_reference(WriterKind kind)
    {
        using var destination = new WriterCase(kind);
        Append(destination.Writer, new byte[] { 0xaa });
        var value = new WriterCountValue(7, 9)
        {
            // The formatter has flushed, so its old span is no longer live. This direct advance
            // belongs to the destination, but bypasses the serializer's counting writer.
            AliasElement = true,
            AfterFlush = () => Append(destination.Writer, new byte[] { 0xc0 })
        };

        var message = new WriterCountFormattedMessage(value);
        _formatted.Serialize(message, destination.Writer).Should().Be(5);

        destination.WrittenMemory.ToArray().Should().Equal(0xaa, 0x81, 0x01, 0x93, 7, 0xc0, 9);
        _formatted.SizeHint(message).Should().Be(6);
        var recovered = _formatted.Deserialize(new ReadOnlySequence<byte>(destination.WrittenMemory.Slice(1)),
            "writer-count-formatted-v1").Should().BeOfType<WriterCountFormattedMessage>().Subject;
        recovered.Value.First.Should().Be(7);
        recovered.Value.Second.Should().Be(9);
    }

    [Fact(DisplayName = "Should_PreserveAliasCounting_When_FormatterIsReachableOnlyThroughNativeCollection")]
    public void Should_preserve_alias_counting_when_formatter_is_reachable_only_through_native_collection()
    {
        var serializer = new WriterCountCollectionSerializer((ExtendedActorSystem)_system);
        using var destination = new PooledPayloadWriter();
        var value = new WriterCountValue(7, 9)
        {
            AliasElement = true,
            AfterFlush = () => Append(destination, new byte[] { 0xc0 })
        };
        var message = new WriterCountCollectionMessage(new[] { value });

        serializer.Serialize(message, destination).Should().Be(6);

        destination.WrittenMemory.ToArray().Should().Equal(0x81, 0x01, 0x91, 0x93, 7, 0xc0, 9);
        serializer.SizeHint(message).Should().Be(7);
    }

    [Theory(DisplayName = "Should_FailBeforeMessageDispatch_When_DestinationIsDisposed")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_fail_before_message_dispatch_when_destination_is_disposed(bool opaque)
    {
        using var pooled = new PooledPayloadWriter();
        IBufferWriter<byte> writer = opaque ? new OpaqueWriter(pooled) : pooled;
        pooled.Dispose();
        var value = new WriterCountValue(7, 9);

        Action unsupported = () => _plain.Serialize(new object(), writer);
        Action formatted = () => _formatted.Serialize(new WriterCountFormattedMessage(value), writer);

        unsupported.Should().Throw<ObjectDisposedException>();
        formatted.Should().Throw<ObjectDisposedException>();
        value.WriteCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Should_FailBeforeUnsupportedMessageDispatch_When_DestinationHasReachedItsCap")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_fail_before_unsupported_message_dispatch_when_destination_has_reached_its_cap(bool opaque)
    {
        using var pooled = new PooledPayloadWriter(initialCapacityHint: 64, maxCapacity: 64);
        IBufferWriter<byte> writer = opaque ? new OpaqueWriter(pooled) : pooled;
        Append(writer, new byte[64]);

        Action serialize = () => _plain.Serialize(new object(), writer);

        var failure = serialize.Should().Throw<PayloadSizeExceededException>().Which;
        failure.MaxCapacity.Should().Be(64);
        failure.AttemptedSize.Should().BeGreaterThan(64);
        pooled.WrittenCount.Should().Be(64);
    }

    [Theory(DisplayName = "Should_PreserveCommittedPrefixAndAllowReuse_When_GeneratedPayloadExceedsDestinationCap")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_preserve_committed_prefix_and_allow_reuse_when_generated_payload_exceeds_destination_cap(bool opaque)
    {
        using var pooled = new PooledPayloadWriter(initialCapacityHint: 64, maxCapacity: 64);
        IBufferWriter<byte> writer = opaque ? new OpaqueWriter(pooled) : pooled;
        Append(writer, new byte[] { 0xaa });
        var payload = new byte[1024];
        var expected = new byte[] { 0xaa, 0x81, 0x01, 0xc5, 0x04, 0x00 }.Concat(payload);

        Action serialize = () => _plain.Serialize(new WriterCountMessage(payload), writer);

        var failure = serialize.Should().Throw<PayloadSizeExceededException>().Which;
        failure.MaxCapacity.Should().Be(64);
        failure.AttemptedSize.Should().BeGreaterThan(64);
        pooled.WrittenCount.Should().BeGreaterThan(1, "the failure follows a payload commit");
        pooled.WrittenMemory.ToArray().Should().Equal(expected.Take(pooled.WrittenCount));

        pooled.Reset();
        _plain.Serialize(new WriterCountMessage(new byte[] { 13 }), writer).Should().Be(5);
        pooled.WrittenMemory.ToArray().Should().Equal(0x81, 0x01, 0xc4, 0x01, 13);
    }

    private static void Append(IBufferWriter<byte> writer, byte[] bytes)
    {
        bytes.AsSpan().CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    public enum WriterKind
    {
        Pooled,
        Array,
        Opaque
    }

    private sealed class WriterCase : IDisposable
    {
        private readonly PooledPayloadWriter? _pooled;
        private readonly ArrayBufferWriter<byte>? _array;

        public WriterCase(WriterKind kind)
        {
            if (kind == WriterKind.Pooled)
            {
                _pooled = new PooledPayloadWriter(initialCapacityHint: 64);
                Writer = _pooled;
            }
            else
            {
                _array = new ArrayBufferWriter<byte>(16);
                Writer = kind == WriterKind.Opaque ? new OpaqueWriter(_array) : _array;
            }
        }

        public IBufferWriter<byte> Writer { get; }
        public ReadOnlyMemory<byte> WrittenMemory => _pooled?.WrittenMemory ?? _array!.WrittenMemory;
        public void Dispose() => _pooled?.Dispose();
    }

    private sealed class OpaqueWriter(IBufferWriter<byte> inner) : IBufferWriter<byte>
    {
        public List<int> AdvanceCounts { get; } = new();

        public void Advance(int count)
        {
            inner.Advance(count);
            AdvanceCounts.Add(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);
        public Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);
    }
}

public interface IWriterCountProtocol
{
}

[AkkaSerializable(Manifest = "writer-count-v1")]
public sealed record WriterCountMessage([property: AkkaField(1)] byte[] Bytes) : IWriterCountProtocol;

[AkkaSerializer<IWriterCountProtocol>("writer-count", 128101)]
public sealed partial class WriterCountSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

public interface IWriterCountFormattedProtocol
{
}

[AkkaSerializable(Manifest = "writer-count-formatted-v1")]
public sealed record WriterCountFormattedMessage([property: AkkaField(1)] WriterCountValue Value) : IWriterCountFormattedProtocol;

[AkkaSerializer<IWriterCountFormattedProtocol>("writer-count-formatted", 128102)]
[AkkaSerializerFormatter<WriterCountValue, WriterCountValueFormatter>]
public sealed partial class WriterCountFormattedSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

public interface IWriterCountCollectionProtocol
{
}

[AkkaSerializable(Manifest = "writer-count-collection-v1")]
public sealed record WriterCountCollectionMessage([property: AkkaField(1)] WriterCountValue[] Values) : IWriterCountCollectionProtocol;

[AkkaSerializer<IWriterCountCollectionProtocol>("writer-count-collection", 128103)]
[AkkaSerializerFormatter<WriterCountValue, WriterCountValueFormatter>]
public sealed partial class WriterCountCollectionSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

public sealed class WriterCountValue(byte first, byte second)
{
    public byte First { get; } = first;
    public byte Second { get; } = second;
    public Action? AfterFlush { get; init; }
    public Exception? Failure { get; init; }
    public bool AliasElement { get; init; }
    public int WriteCalls { get; internal set; }
}

public sealed class WriterCountValueFormatter : IAkkaMessagePackFormatter<WriterCountValue>
{
    public void Write(ref MessagePackWriter writer, WriterCountValue value)
    {
        value.WriteCalls++;
        writer.WriteArrayHeader(value.AliasElement ? 3 : 2);
        writer.Write(value.First);
        writer.Flush();
        value.AfterFlush?.Invoke();
        writer.Write(value.Second);
        if (value.Failure is { } failure)
            throw failure;
    }

    public WriterCountValue Read(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        if (count is not (2 or 3))
            throw new SerializationException("Expected two writer-count values with an optional nil between them.");
        var first = reader.ReadByte();
        if (count == 3)
            reader.ReadNil();
        return new WriterCountValue(first, reader.ReadByte());
    }

    public int SizeOf(WriterCountValue value) => MessagePackSizes.SizeOfArrayHeader(value.AliasElement ? 3 : 2)
        + MessagePackSizes.SizeOfInt32(value.First) + MessagePackSizes.SizeOfInt32(value.Second)
        + (value.AliasElement ? MessagePackSizes.SizeOfNil() : 0);
}
