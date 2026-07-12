//-----------------------------------------------------------------------
// <copyright file="CollectionFieldSpec.cs" company="Akka.NET Project">
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

/// <summary>
/// Native collection support (<c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
/// <c>Dictionary&lt;TKey,TValue&gt;</c>) for the Akka.Serialization.V2 generator, including nested
/// collections, null-vs-empty distinction, nullable value elements, and dictionaries with non-string
/// keys. The WIRE-FORMAT GOLDEN tests freeze the exact bytes so any future encoding drift fails loudly.
/// </summary>
public sealed class CollectionFieldSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private CollectionTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("collection-field-spec");
        _serializer = new CollectionTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: populated
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a populated int array")]
    public void Should_round_trip_populated_int_array()
    {
        var message = new IntArrayMessage(new[] { 10, 20, 30 });
        RoundTrip(message).Values.Should().Equal(10, 20, 30);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated List<string>")]
    public void Should_round_trip_populated_string_list()
    {
        var message = new StringListMessage(new List<string> { "alpha", "beta", "gamma" });
        RoundTrip(message).Names.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated IReadOnlyList of nested objects")]
    public void Should_round_trip_populated_reading_readonly_list()
    {
        var message = new ReadingListMessage(new List<CollReading>
        {
            new("s-1", 1.5),
            new("s-2", 2.5)
        });

        RoundTrip(message).Readings.Should().Equal(new CollReading("s-1", 1.5), new CollReading("s-2", 2.5));
    }

    [Fact(DisplayName = "Generated serializer should round-trip a Dictionary with non-string keys")]
    public void Should_round_trip_dictionary_with_non_string_keys()
    {
        var message = new IntStringDictMessage(new Dictionary<int, string>
        {
            [1] = "one",
            [2] = "two",
            [3] = "three"
        });

        RoundTrip(message).Map.Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [1] = "one",
            [2] = "two",
            [3] = "three"
        });
    }

    [Fact(DisplayName = "Generated serializer should round-trip a Dictionary keyed by Guid")]
    public void Should_round_trip_dictionary_with_guid_keys()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var message = new GuidReadingDictMessage(new Dictionary<Guid, CollReading>
        {
            [a] = new("s-a", 1.0),
            [b] = new("s-b", 2.0)
        });

        var recovered = RoundTrip(message);
        recovered.Map.Should().BeEquivalentTo(new Dictionary<Guid, CollReading>
        {
            [a] = new("s-a", 1.0),
            [b] = new("s-b", 2.0)
        });
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: nested composition
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip nested List<List<int>>")]
    public void Should_round_trip_nested_int_lists()
    {
        var message = new NestedListMessage(new List<List<int>>
        {
            new() { 1, 2 },
            new() { 3 },
            new()
        });

        var recovered = RoundTrip(message);
        recovered.Matrix.Should().HaveCount(3);
        recovered.Matrix[0].Should().Equal(1, 2);
        recovered.Matrix[1].Should().Equal(3);
        recovered.Matrix[2].Should().BeEmpty();
    }

    [Fact(DisplayName = "Generated serializer should round-trip Dictionary<string, List<Reading>>")]
    public void Should_round_trip_dictionary_of_reading_lists()
    {
        var message = new DictOfListMessage(new Dictionary<string, List<CollReading>>
        {
            ["group-a"] = new() { new("s-1", 1.0), new("s-2", 2.0) },
            ["group-b"] = new() { new("s-3", 3.0) }
        });

        var recovered = RoundTrip(message);
        recovered.Grouped.Should().ContainKey("group-a");
        recovered.Grouped["group-a"].Should().Equal(new CollReading("s-1", 1.0), new CollReading("s-2", 2.0));
        recovered.Grouped["group-b"].Should().Equal(new CollReading("s-3", 3.0));
    }

    [Fact(DisplayName = "Generated serializer should round-trip an array of nested objects")]
    public void Should_round_trip_array_of_nested_objects()
    {
        var message = new ReadingArrayMessage(new[] { new CollReading("s-1", 1.0), new CollReading("s-2", 2.0) });
        RoundTrip(message).Readings.Should().Equal(new CollReading("s-1", 1.0), new CollReading("s-2", 2.0));
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: null vs empty (distinct)
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a null nullable collection as null")]
    public void Should_round_trip_null_collection_as_null()
    {
        var message = new NullableCollectionsMessage(null, null, null);
        var recovered = RoundTrip(message);

        recovered.MaybeInts.Should().BeNull();
        recovered.MaybeMap.Should().BeNull();
        recovered.MaybeReadings.Should().BeNull();
    }

    [Fact(DisplayName = "Generated serializer should round-trip an empty collection as empty (distinct from null)")]
    public void Should_round_trip_empty_collection_as_empty()
    {
        var message = new NullableCollectionsMessage(
            new List<int>(),
            new Dictionary<string, int>(),
            Array.Empty<CollReading>());
        var recovered = RoundTrip(message);

        recovered.MaybeInts.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeMap.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeReadings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact(DisplayName = "Generated serializer should encode null and empty collections as different bytes")]
    public void Should_encode_null_and_empty_collections_as_different_bytes()
    {
        var nullBytes = _serializer.ToBinary(new NullableIntArrayMessage(null));
        var emptyBytes = _serializer.ToBinary(new NullableIntArrayMessage(Array.Empty<int>()));

        // nil (0xc0) vs array header 0 (0x90): distinct on the wire.
        nullBytes.Should().Equal(0x81, 0x01, 0xc0);
        emptyBytes.Should().Equal(0x81, 0x01, 0x90);
        nullBytes.Should().NotEqual(emptyBytes);
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: nullable value elements
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a List of nullable value elements")]
    public void Should_round_trip_list_of_nullable_value_elements()
    {
        var message = new NullableElementListMessage(new List<int?> { 1, null, 3, null });
        RoundTrip(message).OptionalInts.Should().Equal(1, null, 3, null);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a List of enum elements")]
    public void Should_round_trip_list_of_enum_elements()
    {
        var message = new EnumListMessage(new List<CollStatus> { CollStatus.A, CollStatus.C, CollStatus.B });
        RoundTrip(message).Statuses.Should().Equal(CollStatus.A, CollStatus.C, CollStatus.B);
    }

    // ------------------------------------------------------------------------------------------
    // Equality: structural round-trip equality
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should preserve structural equality of a collection-bearing record")]
    public void Should_preserve_structural_equality_of_collection_record()
    {
        var message = new EquatableListMessage(new List<CollReading> { new("s-1", 1.0), new("s-2", 2.0) });
        RoundTrip(message).Should().Be(message);
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: jagged arrays (array-of-array composition)
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a populated jagged int array")]
    public void Should_round_trip_populated_jagged_int_array()
    {
        var message = new JaggedIntArrayMessage(new[] { new[] { 1, 2 }, new[] { 3 } });
        var recovered = RoundTrip(message);

        recovered.Rows.Should().NotBeNull();
        recovered.Rows!.Length.Should().Be(2);
        recovered.Rows[0].Should().Equal(1, 2);
        recovered.Rows[1].Should().Equal(3);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a jagged int array with an empty inner array")]
    public void Should_round_trip_jagged_int_array_with_empty_inner()
    {
        var message = new JaggedIntArrayMessage(new[] { Array.Empty<int>(), new[] { 5 } });
        var recovered = RoundTrip(message);

        recovered.Rows.Should().NotBeNull();
        recovered.Rows!.Length.Should().Be(2);
        recovered.Rows[0].Should().BeEmpty();
        recovered.Rows[1].Should().Equal(5);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a null outer jagged array as null")]
    public void Should_round_trip_null_outer_jagged_array()
    {
        var recovered = RoundTrip(new JaggedIntArrayMessage(null));
        recovered.Rows.Should().BeNull();
    }

    [Fact(DisplayName = "Generated serializer should round-trip a jagged array of nested objects")]
    public void Should_round_trip_jagged_array_of_nested_objects()
    {
        var message = new ReadingGridMessage(new[]
        {
            new[] { new CollReading("s-1", 1.0) },
            new[] { new CollReading("s-2", 2.0), new CollReading("s-3", 3.0) }
        });
        var recovered = RoundTrip(message);

        recovered.Grid.Length.Should().Be(2);
        recovered.Grid[0].Should().Equal(new CollReading("s-1", 1.0));
        recovered.Grid[1].Should().Equal(new CollReading("s-2", 2.0), new CollReading("s-3", 3.0));
    }

    // ------------------------------------------------------------------------------------------
    // Size hint exactness (collections participate in exact sizing)
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should report exact size hints for collection messages")]
    public void Should_report_exact_size_hints_for_collection_messages()
    {
        var intArray = new IntArrayMessage(new[] { 10, 20, 30 });
        var stringList = new StringListMessage(new List<string> { "alpha", "beta" });
        var readingList = new ReadingListMessage(new List<CollReading> { new("s-1", 1.5), new("s-2", 2.5) });
        var dict = new IntStringDictMessage(new Dictionary<int, string> { [1] = "one", [2] = "two" });
        var nested = new NestedListMessage(new List<List<int>> { new() { 1, 2 }, new() { 3 } });
        var nullable = new NullableCollectionsMessage(null, null, null);
        var nullableElements = new NullableElementListMessage(new List<int?> { 1, null, 3 });
        var enums = new EnumListMessage(new List<CollStatus> { CollStatus.A, CollStatus.C });
        var jagged = new JaggedIntArrayMessage(new[] { new[] { 1, 2 }, Array.Empty<int>() });
        var grid = new ReadingGridMessage(new[] { new[] { new CollReading("s-1", 1.0) } });

        _serializer.SizeHint(jagged).Should().Be(_serializer.ToBinary(jagged).Length);
        _serializer.SizeHint(grid).Should().Be(_serializer.ToBinary(grid).Length);
        _serializer.SizeHint(enums).Should().Be(_serializer.ToBinary(enums).Length);
        _serializer.SizeHint(intArray).Should().Be(_serializer.ToBinary(intArray).Length);
        _serializer.SizeHint(stringList).Should().Be(_serializer.ToBinary(stringList).Length);
        _serializer.SizeHint(readingList).Should().Be(_serializer.ToBinary(readingList).Length);
        _serializer.SizeHint(dict).Should().Be(_serializer.ToBinary(dict).Length);
        _serializer.SizeHint(nested).Should().Be(_serializer.ToBinary(nested).Length);
        _serializer.SizeHint(nullable).Should().Be(_serializer.ToBinary(nullable).Length);
        _serializer.SizeHint(nullableElements).Should().Be(_serializer.ToBinary(nullableElements).Length);
    }

    // ------------------------------------------------------------------------------------------
    // WIRE-FORMAT GOLDEN TESTS (exact bytes -- the permanence guarantee)
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "GOLDEN: int array encodes as MessagePack array framing")]
    public void Golden_int_array()
    {
        var bytes = _serializer.ToBinary(new IntArrayMessage(new[] { 10, 20, 30 }));

        // 81            map header, 1 field
        //   01          field id 1
        //   93          array header, 3 elements
        //     0a 14 1e  ints 10, 20, 30
        bytes.Should().Equal(0x81, 0x01, 0x93, 0x0a, 0x14, 0x1e);
    }

    [Fact(DisplayName = "GOLDEN: empty array encodes as a zero-length array header")]
    public void Golden_empty_array()
    {
        var bytes = _serializer.ToBinary(new NullableIntArrayMessage(Array.Empty<int>()));
        bytes.Should().Equal(0x81, 0x01, 0x90);
    }

    [Fact(DisplayName = "GOLDEN: null nullable array encodes as nil")]
    public void Golden_null_array()
    {
        var bytes = _serializer.ToBinary(new NullableIntArrayMessage(null));
        bytes.Should().Equal(0x81, 0x01, 0xc0);
    }

    [Fact(DisplayName = "GOLDEN: Dictionary<int,string> encodes as MessagePack map framing")]
    public void Golden_int_string_dictionary()
    {
        var bytes = _serializer.ToBinary(new IntStringDictMessage(new Dictionary<int, string> { [5] = "hi" }));

        // 81            map header, 1 field
        //   01          field id 1
        //   81          map header, 1 entry
        //     05        key int 5
        //     a2 68 69  value "hi"
        bytes.Should().Equal(0x81, 0x01, 0x81, 0x05, 0xa2, 0x68, 0x69);
    }

    [Fact(DisplayName = "GOLDEN: List of nested objects encodes as an array of field-id maps")]
    public void Golden_reading_list()
    {
        var bytes = _serializer.ToBinary(new ReadingListMessage(new List<CollReading> { new("s", 1.5) }));

        // 81                          map header, 1 field
        //   01                        field id 1
        //   91                        array header, 1 element
        //     82                      CollReading map header, 2 fields
        //       01 a1 73              field 1 = "s"
        //       02 cb 3ff8000000000000  field 2 = double 1.5
        bytes.Should().Equal(
            0x81, 0x01, 0x91,
            0x82,
            0x01, 0xa1, 0x73,
            0x02, 0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
    }

    [Fact(DisplayName = "GOLDEN: nested List<List<int>> encodes as nested array framing")]
    public void Golden_nested_int_lists()
    {
        var bytes = _serializer.ToBinary(new NestedListMessage(new List<List<int>>
        {
            new() { 1, 2 },
            new() { 3 }
        }));

        // 81         map header, 1 field
        //   01       field id 1
        //   92       outer array header, 2 elements
        //     92     inner array header, 2 elements
        //       01 02
        //     91     inner array header, 1 element
        //       03
        bytes.Should().Equal(0x81, 0x01, 0x92, 0x92, 0x01, 0x02, 0x91, 0x03);
    }

    [Fact(DisplayName = "GOLDEN: enum list encodes each element as an int32")]
    public void Golden_enum_list()
    {
        var bytes = _serializer.ToBinary(new EnumListMessage(new List<CollStatus> { CollStatus.B }));

        // 81      map header, 1 field
        //   01    field id 1
        //   91    array header, 1 element
        //     01  enum CollStatus.B == 1 written as int32
        bytes.Should().Equal(0x81, 0x01, 0x91, 0x01);
    }

    [Fact(DisplayName = "GOLDEN: jagged int array encodes as nested array framing (identical to List<List<int>>)")]
    public void Golden_jagged_int_array()
    {
        var bytes = _serializer.ToBinary(new JaggedIntArrayMessage(new[] { new[] { 1, 2 }, Array.Empty<int>() }));

        // 81         map header, 1 field
        //   01       field id 1
        //   92       outer array header, 2 elements
        //     92     inner array header, 2 elements
        //       01 02
        //     90     inner array header, 0 elements (empty inner array)
        bytes.Should().Equal(0x81, 0x01, 0x92, 0x92, 0x01, 0x02, 0x90);
    }

    [Fact(DisplayName = "GOLDEN: null outer jagged array encodes as nil")]
    public void Golden_null_outer_jagged_array()
    {
        var bytes = _serializer.ToBinary(new JaggedIntArrayMessage(null));
        bytes.Should().Equal(0x81, 0x01, 0xc0);
    }

    // ------------------------------------------------------------------------------------------
    // Forward compatibility: unknown collection field is skipped whole
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should skip an unknown collection field")]
    public void Should_skip_unknown_collection_field()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(99);
        writer.WriteArrayHeader(2);
        writer.Write(7);
        writer.Write(8);
        writer.Write(1);
        writer.WriteArrayHeader(2);
        writer.Write(10);
        writer.Write(20);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "coll-int-array-v1");
        deserialized.Should().BeOfType<IntArrayMessage>().Subject.Values.Should().Equal(10, 20);
    }

    [Fact(DisplayName = "Generated serializer should reject a nil-on-the-wire non-nullable collection")]
    public void Should_reject_nil_for_non_nullable_collection()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.WriteNil();
        writer.Flush();

        Action deserialize = () => _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "coll-int-array-v1");
        deserialize.Should().Throw<SerializationException>().WithMessage("*Missing required field [Values]*");
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, ICollectionProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface ICollectionProtocol
{
}

[AkkaSerializer(Name = "collection-test", SerializerId = 120105)]
public sealed partial class CollectionTestSerializer : MessagePackSerializer<ICollectionProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable]
public sealed record CollReading(
    [property: AkkaField(1)] string SensorId,
    [property: AkkaField(2)] double Value);

[AkkaSerializable(Manifest = "coll-int-array-v1")]
public sealed record IntArrayMessage(
    [property: AkkaField(1)] int[] Values) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-nullable-int-array-v1")]
public sealed record NullableIntArrayMessage(
    [property: AkkaField(1)] int[]? Values) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-string-list-v1")]
public sealed record StringListMessage(
    [property: AkkaField(1)] List<string> Names) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-reading-list-v1")]
public sealed record ReadingListMessage(
    [property: AkkaField(1)] IReadOnlyList<CollReading> Readings) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-reading-array-v1")]
public sealed record ReadingArrayMessage(
    [property: AkkaField(1)] CollReading[] Readings) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-int-string-dict-v1")]
public sealed record IntStringDictMessage(
    [property: AkkaField(1)] Dictionary<int, string> Map) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-guid-reading-dict-v1")]
public sealed record GuidReadingDictMessage(
    [property: AkkaField(1)] Dictionary<Guid, CollReading> Map) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-nested-list-v1")]
public sealed record NestedListMessage(
    [property: AkkaField(1)] List<List<int>> Matrix) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-dict-of-list-v1")]
public sealed record DictOfListMessage(
    [property: AkkaField(1)] Dictionary<string, List<CollReading>> Grouped) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-nullable-v1")]
public sealed record NullableCollectionsMessage(
    [property: AkkaField(1)] List<int>? MaybeInts,
    [property: AkkaField(2)] Dictionary<string, int>? MaybeMap,
    [property: AkkaField(3)] CollReading[]? MaybeReadings) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-nullable-elements-v1")]
public sealed record NullableElementListMessage(
    [property: AkkaField(1)] List<int?> OptionalInts) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-jagged-int-v1")]
public sealed record JaggedIntArrayMessage(
    [property: AkkaField(1)] int[][]? Rows) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-reading-grid-v1")]
public sealed record ReadingGridMessage(
    [property: AkkaField(1)] CollReading[][] Grid) : ICollectionProtocol;

public enum CollStatus
{
    A = 0,
    B = 1,
    C = 2
}

[AkkaSerializable(Manifest = "coll-enum-list-v1")]
public sealed record EnumListMessage(
    [property: AkkaField(1)] List<CollStatus> Statuses) : ICollectionProtocol;

[AkkaSerializable(Manifest = "coll-equatable-v1")]
public sealed record EquatableListMessage(
    [property: AkkaField(1)] List<CollReading> Readings) : ICollectionProtocol
{
    public bool Equals(EquatableListMessage? other)
        => other is not null && Readings.SequenceEqual(other.Readings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var reading in Readings)
            hash.Add(reading);
        return hash.ToHashCode();
    }
}
