//-----------------------------------------------------------------------
// <copyright file="ImmutableCollectionFieldSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Immutable/read-only collection support (openspec task 5.7) for the Akka.Serialization.V2
/// generator: <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableList&lt;T&gt;</c>,
/// <c>ImmutableHashSet&lt;T&gt;</c>, <c>ImmutableDictionary&lt;TKey,TValue&gt;</c>,
/// <c>IReadOnlyCollection&lt;T&gt;</c>, and <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>.
///
/// These shapes share the EXACT SAME MessagePack wire framing as the four natively-supported
/// shapes covered by <see cref="CollectionFieldSpec"/> (<c>T[]</c>, <c>List&lt;T&gt;</c>,
/// <c>IReadOnlyList&lt;T&gt;</c>, <c>Dictionary&lt;TKey,TValue&gt;</c>) -- the wire-identity tests
/// below assert that directly. Set/map iteration order is NOT guaranteed for
/// <c>ImmutableHashSet&lt;T&gt;</c>/<c>ImmutableDictionary&lt;TKey,TValue&gt;</c>, so multi-element
/// instances of those two kinds are compared by round-tripped VALUE (sorted), never by raw bytes;
/// single-element instances have no ordering ambiguity and are byte-asserted like everything else.
/// </summary>
public sealed class ImmutableCollectionFieldSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private ImmutableCollectionTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("immutable-collection-field-spec");
        _serializer = new ImmutableCollectionTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: populated
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a populated ImmutableArray<int>")]
    public void Should_round_trip_populated_immutable_array()
    {
        var message = new ImmutableArrayMessage(ImmutableArray.Create(10, 20, 30));
        var recovered = RoundTrip(message);
        recovered.Values.IsDefault.Should().BeFalse();
        recovered.Values.Should().Equal(10, 20, 30);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated ImmutableList<string>")]
    public void Should_round_trip_populated_immutable_list()
    {
        var message = new ImmutableListMessage(ImmutableList.Create("alpha", "beta", "gamma"));
        RoundTrip(message).Names.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated ImmutableHashSet<int>")]
    public void Should_round_trip_populated_immutable_hashset()
    {
        var message = new ImmutableHashSetMessage(ImmutableHashSet.Create(1, 2, 3));
        RoundTrip(message).Values.OrderBy(x => x).Should().Equal(1, 2, 3);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated ImmutableDictionary<string,int>")]
    public void Should_round_trip_populated_immutable_dictionary()
    {
        var message = new ImmutableDictionaryMessage(ImmutableDictionary<string, int>.Empty
            .Add("one", 1).Add("two", 2).Add("three", 3));
        var recovered = RoundTrip(message);
        recovered.Map.Should().BeEquivalentTo(new Dictionary<string, int> { ["one"] = 1, ["two"] = 2, ["three"] = 3 });
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated IReadOnlyCollection<int>")]
    public void Should_round_trip_populated_readonly_collection()
    {
        var message = new ReadOnlyCollectionMessage(new List<int> { 1, 2, 3 });
        RoundTrip(message).Values.Should().Equal(1, 2, 3);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated IReadOnlyDictionary<string,int>")]
    public void Should_round_trip_populated_readonly_dictionary()
    {
        var message = new ReadOnlyDictionaryMessage(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        RoundTrip(message).Map.Should().BeEquivalentTo(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: nested composition
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip an ImmutableList of nested [AkkaSerializable] objects")]
    public void Should_round_trip_immutable_list_of_nested_objects()
    {
        var message = new ImmutableListOfNestedMessage(ImmutableList.Create(
            new ImmReading("s-1", 1.5),
            new ImmReading("s-2", 2.5)));

        RoundTrip(message).Readings.Should().Equal(new ImmReading("s-1", 1.5), new ImmReading("s-2", 2.5));
    }

    [Fact(DisplayName = "Generated serializer should round-trip ImmutableDictionary<string, List<int>>")]
    public void Should_round_trip_immutable_dictionary_of_lists()
    {
        var message = new ImmutableDictOfListMessage(ImmutableDictionary<string, List<int>>.Empty
            .Add("group-a", new List<int> { 1, 2 })
            .Add("group-b", new List<int> { 3 }));

        var recovered = RoundTrip(message);
        recovered.Grouped.Should().ContainKey("group-a");
        recovered.Grouped["group-a"].Should().Equal(1, 2);
        recovered.Grouped["group-b"].Should().Equal(3);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a nested ImmutableList<ImmutableArray<int>>")]
    public void Should_round_trip_nested_immutable_list_of_immutable_arrays()
    {
        var message = new NestedImmutableMessage(ImmutableList.Create(
            ImmutableArray.Create(1, 2),
            ImmutableArray.Create(3),
            ImmutableArray<int>.Empty));

        var recovered = RoundTrip(message);
        recovered.Matrix.Should().HaveCount(3);
        recovered.Matrix[0].Should().Equal(1, 2);
        recovered.Matrix[1].Should().Equal(3);
        recovered.Matrix[2].IsDefault.Should().BeFalse();
        recovered.Matrix[2].Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: null vs empty (distinct) for the reference-typed shapes
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip null nullable immutable/read-only collections as null")]
    public void Should_round_trip_null_immutable_collections_as_null()
    {
        var message = new NullableImmutableMessage(null, null, null, null, null);
        var recovered = RoundTrip(message);

        recovered.MaybeList.Should().BeNull();
        recovered.MaybeHashSet.Should().BeNull();
        recovered.MaybeDictionary.Should().BeNull();
        recovered.MaybeReadOnlyCollection.Should().BeNull();
        recovered.MaybeReadOnlyDictionary.Should().BeNull();
    }

    [Fact(DisplayName = "Generated serializer should round-trip empty immutable/read-only collections as empty (distinct from null)")]
    public void Should_round_trip_empty_immutable_collections_as_empty()
    {
        var message = new NullableImmutableMessage(
            ImmutableList<int>.Empty,
            ImmutableHashSet<int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            new List<int>(),
            new Dictionary<string, int>());
        var recovered = RoundTrip(message);

        recovered.MaybeList.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeHashSet.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeDictionary.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeReadOnlyCollection.Should().NotBeNull().And.BeEmpty();
        recovered.MaybeReadOnlyDictionary.Should().NotBeNull().And.BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // ImmutableArray<T>: default(ImmutableArray<T>).IsDefault vs .Empty (the value-type-specific
    // null-ish distinction -- see the design note above EmitWriteCollectionBody in the generator).
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip default(ImmutableArray<T>) as itself (IsDefault true survives the round trip)")]
    public void Should_round_trip_default_immutable_array_as_default()
    {
        var message = new ImmutableArrayMessage(default);
        message.Values.IsDefault.Should().BeTrue();

        var recovered = RoundTrip(message);
        recovered.Values.IsDefault.Should().BeTrue();
    }

    [Fact(DisplayName = "Generated serializer should round-trip ImmutableArray<T>.Empty as empty, distinct from default")]
    public void Should_round_trip_empty_immutable_array_as_empty()
    {
        var message = new ImmutableArrayMessage(ImmutableArray<int>.Empty);
        message.Values.IsDefault.Should().BeFalse();

        var recovered = RoundTrip(message);
        recovered.Values.IsDefault.Should().BeFalse();
        recovered.Values.Should().BeEmpty();
    }

    [Fact(DisplayName = "Generated serializer should encode default and empty ImmutableArray<T> as different bytes (nil vs zero-length array header)")]
    public void Should_encode_default_and_empty_immutable_array_as_different_bytes()
    {
        var defaultBytes = _serializer.ToBinary(new ImmutableArrayMessage(default));
        var emptyBytes = _serializer.ToBinary(new ImmutableArrayMessage(ImmutableArray<int>.Empty));

        // nil (0xc0) vs array header 0 (0x90): distinct on the wire, exactly like null vs empty for
        // every reference collection kind.
        defaultBytes.Should().Equal(0x81, 0x01, 0xc0);
        emptyBytes.Should().Equal(0x81, 0x01, 0x90);
        defaultBytes.Should().NotEqual(emptyBytes);
    }

    [Fact(DisplayName = "Generated serializer should accept a nil-on-the-wire REQUIRED ImmutableArray<T> field (default is a legal value, not a missing one)")]
    public void Should_accept_nil_for_required_immutable_array_field()
    {
        // Deliberately different from CollectionFieldSpec's
        // Should_reject_nil_for_non_nullable_collection: ImmutableArray<T> is a VALUE type, so its
        // "required" check only tests field-index presence, never an "is null"-equivalent check --
        // default(ImmutableArray<T>) is a legal value of the type, the same way 0 is a legal value
        // for a required int field. Nil-on-the-wire is exactly how a default(ImmutableArray<T>)
        // serializes (see Should_encode_default_and_empty_immutable_array_as_different_bytes), so it
        // must decode back to default without throwing.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.WriteNil();
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "imm-array-v1");
        deserialized.Should().BeOfType<ImmutableArrayMessage>().Subject.Values.IsDefault.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------
    // Size hint exactness
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should report exact size hints for immutable/read-only collection messages")]
    public void Should_report_exact_size_hints_for_immutable_collection_messages()
    {
        var array = new ImmutableArrayMessage(ImmutableArray.Create(10, 20, 30));
        var defaultArray = new ImmutableArrayMessage(default);
        var list = new ImmutableListMessage(ImmutableList.Create("alpha", "beta"));
        var hashSet = new ImmutableHashSetMessage(ImmutableHashSet.Create(1, 2, 3));
        var dict = new ImmutableDictionaryMessage(ImmutableDictionary<string, int>.Empty.Add("one", 1).Add("two", 2));
        var readOnlyCollection = new ReadOnlyCollectionMessage(new List<int> { 1, 2, 3 });
        var readOnlyDictionary = new ReadOnlyDictionaryMessage(new Dictionary<string, int> { ["a"] = 1 });
        var nested = new NestedImmutableMessage(ImmutableList.Create(ImmutableArray.Create(1, 2), ImmutableArray<int>.Empty));
        var nullable = new NullableImmutableMessage(null, null, null, null, null);

        _serializer.SizeHint(array).Should().Be(_serializer.ToBinary(array).Length);
        _serializer.SizeHint(defaultArray).Should().Be(_serializer.ToBinary(defaultArray).Length);
        _serializer.SizeHint(list).Should().Be(_serializer.ToBinary(list).Length);
        _serializer.SizeHint(hashSet).Should().Be(_serializer.ToBinary(hashSet).Length);
        _serializer.SizeHint(dict).Should().Be(_serializer.ToBinary(dict).Length);
        _serializer.SizeHint(readOnlyCollection).Should().Be(_serializer.ToBinary(readOnlyCollection).Length);
        _serializer.SizeHint(readOnlyDictionary).Should().Be(_serializer.ToBinary(readOnlyDictionary).Length);
        _serializer.SizeHint(nested).Should().Be(_serializer.ToBinary(nested).Length);
        _serializer.SizeHint(nullable).Should().Be(_serializer.ToBinary(nullable).Length);
    }

    // ------------------------------------------------------------------------------------------
    // WIRE-FORMAT IDENTITY: an immutable/read-only-collection field must be byte-identical to the
    // same data in the equivalent natively-supported (List/Dictionary/array) shape.
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "GOLDEN: ImmutableList<int> encodes byte-identical to List<int> for the same data")]
    public void Golden_immutable_list_matches_list_bytes()
    {
        var immutableBytes = _serializer.ToBinary(new ImmutableListIntMessage(ImmutableList.Create(10, 20, 30)));
        var listBytes = _serializer.ToBinary(new PlainListIntMessage(new List<int> { 10, 20, 30 }));

        immutableBytes.Should().Equal(listBytes);
        // 81 map header, 1 field; 01 field id 1; 93 array header 3; 0a 14 1e ints 10,20,30.
        immutableBytes.Should().Equal(0x81, 0x01, 0x93, 0x0a, 0x14, 0x1e);
    }

    [Fact(DisplayName = "GOLDEN: ImmutableArray<int> encodes byte-identical to int[] for the same data")]
    public void Golden_immutable_array_matches_array_bytes()
    {
        var immutableBytes = _serializer.ToBinary(new ImmutableArrayMessage(ImmutableArray.Create(10, 20, 30)));
        var arrayBytes = _serializer.ToBinary(new PlainIntArrayMessage(new[] { 10, 20, 30 }));

        immutableBytes.Should().Equal(arrayBytes);
        immutableBytes.Should().Equal(0x81, 0x01, 0x93, 0x0a, 0x14, 0x1e);
    }

    [Fact(DisplayName = "GOLDEN: IReadOnlyCollection<int> encodes byte-identical to List<int> for the same data")]
    public void Golden_readonly_collection_matches_list_bytes()
    {
        var readOnlyBytes = _serializer.ToBinary(new ReadOnlyCollectionMessage(new List<int> { 10, 20, 30 }));
        var listBytes = _serializer.ToBinary(new PlainListIntMessage(new List<int> { 10, 20, 30 }));

        readOnlyBytes.Should().Equal(listBytes);
    }

    [Fact(DisplayName = "GOLDEN: IReadOnlyDictionary<string,int> single-entry encodes byte-identical to Dictionary<string,int>")]
    public void Golden_readonly_dictionary_matches_dictionary_bytes()
    {
        var readOnlyBytes = _serializer.ToBinary(new ReadOnlyDictionaryMessage(new Dictionary<string, int> { ["hi"] = 5 }));
        var dictBytes = _serializer.ToBinary(new PlainStringIntDictMessage(new Dictionary<string, int> { ["hi"] = 5 }));

        readOnlyBytes.Should().Equal(dictBytes);
    }

    [Fact(DisplayName = "GOLDEN: single-element ImmutableHashSet<int> encodes as a one-element MessagePack array")]
    public void Golden_single_element_immutable_hashset()
    {
        var bytes = _serializer.ToBinary(new ImmutableHashSetMessage(ImmutableHashSet.Create(5)));

        // 81 map header, 1 field; 01 field id 1; 91 array header 1; 05 int 5.
        bytes.Should().Equal(0x81, 0x01, 0x91, 0x05);
    }

    [Fact(DisplayName = "GOLDEN: single-entry ImmutableDictionary<string,int> encodes as a one-entry MessagePack map")]
    public void Golden_single_entry_immutable_dictionary()
    {
        var bytes = _serializer.ToBinary(new ImmutableDictionaryMessage(ImmutableDictionary<string, int>.Empty.Add("hi", 5)));

        // 81 map header, 1 field; 01 field id 1; 81 map header 1 entry; a2 68 69 "hi"; 05 int 5.
        bytes.Should().Equal(0x81, 0x01, 0x81, 0xa2, 0x68, 0x69, 0x05);
    }

    [Fact(DisplayName = "GOLDEN: empty ImmutableList<int> encodes as a zero-length array header")]
    public void Golden_empty_immutable_list()
    {
        var bytes = _serializer.ToBinary(new ImmutableListIntMessage(ImmutableList<int>.Empty));
        bytes.Should().Equal(0x81, 0x01, 0x90);
    }

    [Fact(DisplayName = "GOLDEN: null ImmutableList<int>? encodes as nil")]
    public void Golden_null_immutable_list()
    {
        var bytes = _serializer.ToBinary(new NullableImmutableMessage(null, null, null, null, null));
        bytes.Should().Equal(
            0x85,
            0x01, 0xc0,
            0x02, 0xc0,
            0x03, 0xc0,
            0x04, 0xc0,
            0x05, 0xc0);
    }

    // ------------------------------------------------------------------------------------------
    // Round-trip: multi-element ImmutableHashSet<T>/ImmutableDictionary<K,V> -- values only, since
    // iteration order (and therefore wire byte layout) is not guaranteed for these two kinds.
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip a multi-element ImmutableHashSet<int> by value regardless of iteration order")]
    public void Should_round_trip_multi_element_immutable_hashset_by_value()
    {
        var message = new ImmutableHashSetMessage(ImmutableHashSet.Create(5, 1, 9, 3));
        var recovered = RoundTrip(message);

        recovered.Values.OrderBy(x => x).Should().Equal(1, 3, 5, 9);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a multi-entry ImmutableDictionary<string,int> by value regardless of iteration order")]
    public void Should_round_trip_multi_entry_immutable_dictionary_by_value()
    {
        var message = new ImmutableDictionaryMessage(ImmutableDictionary<string, int>.Empty
            .Add("zulu", 26).Add("alpha", 1).Add("mike", 13));
        var recovered = RoundTrip(message);

        recovered.Map.Should().BeEquivalentTo(new Dictionary<string, int> { ["zulu"] = 26, ["alpha"] = 1, ["mike"] = 13 });
    }

    // ------------------------------------------------------------------------------------------
    // Nullable value element composition (ImmutableList<int?>) and nullable-wrapped
    // ImmutableArray<T>? (Nullable<ImmutableArray<T>>, distinct from the struct's own IsDefault).
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should round-trip an ImmutableList of nullable value elements")]
    public void Should_round_trip_immutable_list_of_nullable_elements()
    {
        var message = new NullableElementImmutableListMessage(ImmutableList.Create<int?>(1, null, 3, null));
        RoundTrip(message).OptionalInts.Should().Equal(1, null, 3, null);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a null Nullable<ImmutableArray<T>> field as null")]
    public void Should_round_trip_null_nullable_wrapped_immutable_array()
    {
        var message = new NullableWrappedImmutableArrayMessage(null);
        RoundTrip(message).Values.Should().BeNull();
    }

    [Fact(DisplayName = "Generated serializer should round-trip a populated Nullable<ImmutableArray<T>> field")]
    public void Should_round_trip_populated_nullable_wrapped_immutable_array()
    {
        var message = new NullableWrappedImmutableArrayMessage(ImmutableArray.Create(7, 8, 9));
        var recovered = RoundTrip(message);

        recovered.Values.Should().NotBeNull();
        recovered.Values!.Value.Should().Equal(7, 8, 9);
    }

    // ------------------------------------------------------------------------------------------
    // Forward compatibility: unknown immutable-collection field is skipped whole
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generated serializer should skip an unknown ImmutableDictionary field")]
    public void Should_skip_unknown_immutable_dictionary_field()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(99);
        writer.WriteMapHeader(1);
        writer.Write("ignored");
        writer.Write(1);
        writer.Write(1);
        writer.WriteMapHeader(1);
        writer.Write("one");
        writer.Write(1);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "imm-dict-v1");
        deserialized.Should().BeOfType<ImmutableDictionaryMessage>().Subject.Map.Should().BeEquivalentTo(new Dictionary<string, int> { ["one"] = 1 });
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IImmutableCollectionProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface IImmutableCollectionProtocol
{
}

[AkkaSerializer<IImmutableCollectionProtocol>("immutable-collection-test", 120106)]
public sealed partial class ImmutableCollectionTestSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable]
public sealed record ImmReading(
    [property: AkkaField(1)] string SensorId,
    [property: AkkaField(2)] double Value);

[AkkaSerializable(Manifest = "imm-array-v1")]
public sealed record ImmutableArrayMessage(
    [property: AkkaField(1)] ImmutableArray<int> Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-list-v1")]
public sealed record ImmutableListMessage(
    [property: AkkaField(1)] ImmutableList<string> Names) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-list-int-v1")]
public sealed record ImmutableListIntMessage(
    [property: AkkaField(1)] ImmutableList<int> Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "plain-list-int-v1")]
public sealed record PlainListIntMessage(
    [property: AkkaField(1)] List<int> Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "plain-int-array-v1")]
public sealed record PlainIntArrayMessage(
    [property: AkkaField(1)] int[] Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "plain-string-int-dict-v1")]
public sealed record PlainStringIntDictMessage(
    [property: AkkaField(1)] Dictionary<string, int> Map) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-hashset-v1")]
public sealed record ImmutableHashSetMessage(
    [property: AkkaField(1)] ImmutableHashSet<int> Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-dict-v1")]
public sealed record ImmutableDictionaryMessage(
    [property: AkkaField(1)] ImmutableDictionary<string, int> Map) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-readonly-collection-v1")]
public sealed record ReadOnlyCollectionMessage(
    [property: AkkaField(1)] IReadOnlyCollection<int> Values) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-readonly-dict-v1")]
public sealed record ReadOnlyDictionaryMessage(
    [property: AkkaField(1)] IReadOnlyDictionary<string, int> Map) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-list-of-nested-v1")]
public sealed record ImmutableListOfNestedMessage(
    [property: AkkaField(1)] ImmutableList<ImmReading> Readings) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-dict-of-list-v1")]
public sealed record ImmutableDictOfListMessage(
    [property: AkkaField(1)] ImmutableDictionary<string, List<int>> Grouped) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-nested-v1")]
public sealed record NestedImmutableMessage(
    [property: AkkaField(1)] ImmutableList<ImmutableArray<int>> Matrix) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-nullable-v1")]
public sealed record NullableImmutableMessage(
    [property: AkkaField(1)] ImmutableList<int>? MaybeList,
    [property: AkkaField(2)] ImmutableHashSet<int>? MaybeHashSet,
    [property: AkkaField(3)] ImmutableDictionary<string, int>? MaybeDictionary,
    [property: AkkaField(4)] IReadOnlyCollection<int>? MaybeReadOnlyCollection,
    [property: AkkaField(5)] IReadOnlyDictionary<string, int>? MaybeReadOnlyDictionary) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-nullable-elements-v1")]
public sealed record NullableElementImmutableListMessage(
    [property: AkkaField(1)] ImmutableList<int?> OptionalInts) : IImmutableCollectionProtocol;

[AkkaSerializable(Manifest = "imm-nullable-wrapped-array-v1")]
public sealed record NullableWrappedImmutableArrayMessage(
    [property: AkkaField(1)] ImmutableArray<int>? Values) : IImmutableCollectionProtocol;
