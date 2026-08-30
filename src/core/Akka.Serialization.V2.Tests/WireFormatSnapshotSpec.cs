//-----------------------------------------------------------------------
// <copyright file="WireFormatSnapshotSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using VerifyXunit;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// COMMITTED WIRE-FORMAT SNAPSHOT GATE for Akka.Serialization.V2.
/// </summary>
/// <remarks>
/// <para>
/// Every case below serializes one deterministic, hardcoded message and compares its exact wire
/// bytes -- rendered as a human-reviewable annotated hex dump, never raw <c>.bin</c> -- against a
/// committed <c>WireSnapshots/&lt;case&gt;.verified.txt</c> artifact using Verify
/// (<c>Verify.XunitV3</c>). Unlike the inline <c>GOLDEN:</c> byte-array assertions scattered across
/// the other specs in this project (which pin a handful of hand-picked shapes), this spec's job is
/// breadth: one snapshot per corpus case, reusing the exact message types already declared by the
/// other specs in this project rather than redeclaring parallel fixtures. See
/// <c>WireSnapshots/README.md</c> for the update procedure and the hash-ordering exclusion
/// rationale.
/// </para>
/// <para>
/// Every value in this file is a hardcoded constant -- no <see cref="DateTime.Now"/>, no
/// <see cref="Guid.NewGuid"/> -- so a byte-for-byte mismatch always means the WIRE FORMAT changed,
/// never that the test's own inputs drifted.
/// </para>
/// </remarks>
public sealed class WireFormatSnapshotSpec : IAsyncLifetime
{
    // ------------------------------------------------------------------------------------------
    // Corpus case names -- also the committed snapshot file names
    // (WireSnapshots/<case-name>.verified.txt).
    // ------------------------------------------------------------------------------------------
    private static readonly string[] AllCaseNames =
    {
        // Primitives, nullable fields, sparse indices, nesting.
        "primitives-all-types",
        "nullable-fields-populated",
        "nullable-fields-all-null",
        "sparse-field-indices",
        "nested-message-single-level",
        "nested-message-multi-level",

        // Native collection shapes (T[], List<T>, IReadOnlyList<T>, Dictionary<TKey,TValue>).
        "collection-array-populated",
        "collection-array-null",
        "collection-array-empty",
        "collection-list-populated",
        "collection-list-of-nested-objects",
        "collection-dictionary-non-string-key",

        // Immutable/read-only collection shapes (all 6 -- see design notes in
        // ImmutableCollectionFieldSpec for the hash-ordering exclusion this corpus honors).
        "immutable-array-populated",
        "immutable-array-default",
        "immutable-array-empty",
        "immutable-list-populated",
        "immutable-hashset-single-element",
        "immutable-dictionary-single-entry",
        "readonly-collection-populated",
        "readonly-dictionary-populated",

        // [AkkaUnion]: one snapshot per declared member of IOrderEvent.
        "union-member-order-placed-class",
        "union-member-order-cancelled-nested-only",
        "union-member-order-note-struct",

        // Closed-generic [AkkaSerializable<T>] registrations.
        "closed-generic-wrapper-order-request",
        "closed-generic-wrapper-order-receipt",

        // The combined scenario: a closed-generic wrapper whose payload is itself a union.
        "generic-wrapper-union-order-placed",
        "generic-wrapper-union-order-cancelled",

        // Keyword-named property/constructor-parameter escaping.
        "keyword-named-property",

        // [AkkaEnvelopePayload]-shaped opaque payload: fixed inner serializer id + manifest + bytes.
        "envelope-payload-fixed-inner-serializer",

        // AllowEmpty fieldless message: the smallest possible wire shape (a bare 1-byte map header).
        "fieldless-message-allow-empty",
    };

    private ActorSystem _system = null!;
    private GeneratedTestSerializer _generatedSerializer = null!;
    private CollectionTestSerializer _collectionSerializer = null!;
    private ImmutableCollectionTestSerializer _immutableSerializer = null!;
    private UnionTestSerializer _unionSerializer = null!;
    private ClosedGenericTestSerializer _closedGenericSerializer = null!;
    private GapFixSerializer _gapFixSerializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("wire-format-snapshot-spec");
        var extendedSystem = (ExtendedActorSystem)_system;
        _generatedSerializer = new GeneratedTestSerializer(extendedSystem);
        _collectionSerializer = new CollectionTestSerializer(extendedSystem);
        _immutableSerializer = new ImmutableCollectionTestSerializer(extendedSystem);
        _unionSerializer = new UnionTestSerializer(extendedSystem);
        _closedGenericSerializer = new ClosedGenericTestSerializer(extendedSystem);
        _gapFixSerializer = new GapFixSerializer(extendedSystem);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    public static IEnumerable<object[]> CaseNames() => AllCaseNames.Select(name => new object[] { name });

    [Theory(DisplayName = "Wire format should match its committed snapshot")]
    [MemberData(nameof(CaseNames))]
    public Task Wire_format_should_match_committed_snapshot(string caseName)
    {
        var wireCase = BuildCase(caseName);
        var hexDump = HexDumpFormatter.Format(caseName, wireCase.MessageType, wireCase.Manifest, wireCase.SerializerId, wireCase.Bytes);

        return Verifier.Verify(hexDump)
            .UseDirectory("WireSnapshots")
            .UseFileName(caseName);
    }

    private WireSnapshotCase BuildCase(string caseName) => caseName switch
    {
        // --------------------------------------------------------------------------------------
        // Primitives, nullable fields, sparse indices, nesting -- fixtures from
        // GeneratedMessagePackSerializerSpec.
        // --------------------------------------------------------------------------------------
        "primitives-all-types" => Case(_generatedSerializer, new PrimitiveMessage(
            "order-1",
            42,
            9000000000L,
            true,
            12.5d,
            123.456m,
            Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4"),
            new DateTime(2026, 6, 3, 4, 45, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 6, 3, 4, 45, 0, TimeSpan.FromHours(2)),
            SampleStatus.Accepted,
            ActorRefs.NoSender)),

        "nullable-fields-populated" => Case(_generatedSerializer, new OptionalMessage(
            "optional-1",
            42,
            Guid.Parse("78055b71-1e7a-4a20-8e52-712db4fda457"),
            new DateTime(2026, 6, 6, 12, 30, 0, DateTimeKind.Utc),
            SampleStatus.Accepted,
            "notes",
            new ShippingAddress("1 Main St", "Seattle"))),

        "nullable-fields-all-null" => Case(_generatedSerializer, new OptionalMessage(
            "optional-2", null, null, null, null, null, null)),

        "sparse-field-indices" => Case(_generatedSerializer, new SparseFieldMessage(17, "alpha")),

        "nested-message-single-level" => Case(_generatedSerializer, new ShipmentMessage(
            "order-1", new ShippingAddress("1 Main St", "Seattle"))),

        "nested-message-multi-level" => Case(_generatedSerializer, new WarehouseMessage(
            "warehouse-1", new WarehouseInfo(new WarehouseLocation("Seattle", new CountryInfo("US"))))),

        // --------------------------------------------------------------------------------------
        // Native collection shapes -- fixtures from CollectionFieldSpec.
        // --------------------------------------------------------------------------------------
        "collection-array-populated" => Case(_collectionSerializer, new IntArrayMessage(new[] { 10, 20, 30 })),

        "collection-array-null" => Case(_collectionSerializer, new NullableIntArrayMessage(null)),

        "collection-array-empty" => Case(_collectionSerializer, new NullableIntArrayMessage(Array.Empty<int>())),

        "collection-list-populated" => Case(_collectionSerializer, new StringListMessage(
            new List<string> { "alpha", "beta", "gamma" })),

        "collection-list-of-nested-objects" => Case(_collectionSerializer, new ReadingListMessage(
            new List<CollReading> { new("s-1", 1.5), new("s-2", 2.5) })),

        "collection-dictionary-non-string-key" => Case(_collectionSerializer, new IntStringDictMessage(
            new Dictionary<int, string> { [1] = "one", [2] = "two", [3] = "three" })),

        // --------------------------------------------------------------------------------------
        // Immutable/read-only collection shapes -- fixtures from ImmutableCollectionFieldSpec.
        // ImmutableHashSet/ImmutableDictionary use SINGLE-element instances deliberately: multi-
        // element hash/dictionary iteration order is not stable across runtimes, so multi-element
        // instances of those two kinds are excluded from the byte-snapshot corpus (see
        // WireSnapshots/README.md).
        // --------------------------------------------------------------------------------------
        "immutable-array-populated" => Case(_immutableSerializer, new ImmutableArrayMessage(
            ImmutableArray.Create(10, 20, 30))),

        "immutable-array-default" => Case(_immutableSerializer, new ImmutableArrayMessage(default)),

        "immutable-array-empty" => Case(_immutableSerializer, new ImmutableArrayMessage(ImmutableArray<int>.Empty)),

        "immutable-list-populated" => Case(_immutableSerializer, new ImmutableListMessage(
            ImmutableList.Create("alpha", "beta", "gamma"))),

        "immutable-hashset-single-element" => Case(_immutableSerializer, new ImmutableHashSetMessage(
            ImmutableHashSet.Create(5))),

        "immutable-dictionary-single-entry" => Case(_immutableSerializer, new ImmutableDictionaryMessage(
            ImmutableDictionary<string, int>.Empty.Add("hi", 5))),

        "readonly-collection-populated" => Case(_immutableSerializer, new ReadOnlyCollectionMessage(
            new List<int> { 1, 2, 3 })),

        "readonly-dictionary-populated" => Case(_immutableSerializer, new ReadOnlyDictionaryMessage(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 })),

        // --------------------------------------------------------------------------------------
        // [AkkaUnion] -- one snapshot per declared member of IOrderEvent -- fixtures from
        // GeneratedUnionSpec.
        // --------------------------------------------------------------------------------------
        "union-member-order-placed-class" => Case(_unionSerializer, new UnionEnvelope(
            "env-4", new OrderPlaced("order-4", 7))),

        "union-member-order-cancelled-nested-only" => Case(_unionSerializer, new UnionEnvelope(
            "env-2", new OrderCancelled("order-2", "customer request"))),

        "union-member-order-note-struct" => Case(_unionSerializer, new UnionEnvelope(
            "env-3", new OrderNote("order-3", "expedite"))),

        // --------------------------------------------------------------------------------------
        // Closed-generic [AkkaSerializable<T>] registrations -- fixtures from
        // GeneratedClosedGenericSpec.
        // --------------------------------------------------------------------------------------
        "closed-generic-wrapper-order-request" => Case(_closedGenericSerializer, new Wrapper<OrderRequest>(
            "wrap-1", new OrderRequest("order-1", 5), 3)),

        "closed-generic-wrapper-order-receipt" => Case(_closedGenericSerializer, new Wrapper<OrderReceipt>(
            "wrap-3", new OrderReceipt("receipt-1"), 1)),

        // The combined scenario: a generic wrapper registered as a closed construction whose
        // payload field is a closed manifest-discriminated union.
        "generic-wrapper-union-order-placed" => Case(_closedGenericSerializer, new EventWrapper<IOrderEvent>(
            "evt-1", new OrderPlaced("order-10", 2))),

        "generic-wrapper-union-order-cancelled" => Case(_closedGenericSerializer, new EventWrapper<IOrderEvent>(
            "evt-2", new OrderCancelled("order-11", "late"))),

        // --------------------------------------------------------------------------------------
        // Keyword-named property/constructor-parameter escaping -- fixture from
        // GeneratedMessagePackSerializerSpec.
        // --------------------------------------------------------------------------------------
        "keyword-named-property" => Case(_generatedSerializer, new KeywordNamedMessage("something-happened")),

        // --------------------------------------------------------------------------------------
        // Envelope payload with a fixed inner serializer id, manifest, and byte payload -- fixture
        // from GeneratedMessagePackSerializerSpec.
        // --------------------------------------------------------------------------------------
        "envelope-payload-fixed-inner-serializer" => Case(_generatedSerializer, new OpaqueEnvelope(
            "envelope-1",
            new OpaqueSerializedPayload(
                CustomProtobufPayloadSerializer.IdentifierValue,
                CustomProtobufPayloadSerializer.ManifestName,
                Encoding.UTF8.GetBytes("fake-protobuf|payload-1|17")))),

        // --------------------------------------------------------------------------------------
        // AllowEmpty fieldless message -- fixture from FieldlessAndStructFieldSpec. The smallest
        // possible wire shape: a bare 1-byte empty MessagePack map header (0x80).
        // --------------------------------------------------------------------------------------
        "fieldless-message-allow-empty" => Case(_gapFixSerializer, new GapHeartbeat()),

        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown wire snapshot case.")
    };

    private static WireSnapshotCase Case<TMessage>(AkkaSerializer serializer, TMessage message)
        where TMessage : notnull
    {
        var bytes = serializer.ToBinary(message);
        var manifest = serializer.Manifest(message);
        return new WireSnapshotCase(HexDumpFormatter.FriendlyTypeName(typeof(TMessage)), manifest, serializer.Identifier, bytes);
    }

    private readonly record struct WireSnapshotCase(string MessageType, string Manifest, int SerializerId, byte[] Bytes);
}
