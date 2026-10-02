//-----------------------------------------------------------------------
// <copyright file="WireFormatSnapshotFormatterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using VerifyXunit;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// COMMITTED WIRE-FORMAT SNAPSHOTS for formatter-handled collection members (G-1): a formatter-handled type
/// as a collection element, dictionary key and dictionary value. Same mechanism and update procedure as <see cref="WireFormatSnapshotSpec"/> (see
/// <c>WireSnapshots/README.md</c>); a separate spec so those existing cases, and their snapshots, stay
/// untouched. Every input is a hardcoded constant, so a mismatch always means the wire format changed.
/// </summary>
public sealed class WireFormatSnapshotFormatterSpec : IAsyncLifetime
{
    private static readonly Address Local = new("akka", "sys", "localhost", 2552);
    private static readonly Address Remote = new("akka.tcp", "other", "10.0.0.2", 4053);
    private static readonly Address NoHost = new("akka", "sys");

    private static readonly string[] AllCaseNames =
    {
        // G-1: formatter-handled types in collection element, dictionary key and dictionary value position.
        "formatter-element-list-of-address",
        "formatter-element-nullable-address-list",
        "formatter-key-and-value-address-dictionary",
        "formatter-all-address-shapes-single-element",
        "formatter-custom-reference-type-elements",
        "formatter-custom-value-type-nullable-elements",
        "formatter-actor-ref-elements-override-native",
    };

    private ActorSystem _system = null!;
    private FormatterPositionSerializer _formatterSerializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("wire-format-snapshot-formatter");
        var extendedSystem = (ExtendedActorSystem)_system;
        _formatterSerializer = new FormatterPositionSerializer(extendedSystem);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    public static IEnumerable<object[]> CaseNames() => AllCaseNames.Select(name => new object[] { name });

    [Theory(DisplayName = "Should_MatchCommittedSnapshot_When_FormatterCollectionShapesAreSerialized")]
    [MemberData(nameof(CaseNames))]
    public Task Should_MatchCommittedSnapshot_When_FormatterCollectionShapesAreSerialized(string caseName)
    {
        var wireCase = BuildCase(caseName);
        var hexDump = HexDumpFormatter.Format(caseName, wireCase.MessageType, wireCase.Manifest, wireCase.SerializerId, wireCase.Bytes);

        return Verifier.Verify(hexDump)
            .UseDirectory("WireSnapshots")
            .UseFileName(caseName);
    }

    private WireSnapshotCase BuildCase(string caseName) => caseName switch
    {
        "formatter-element-list-of-address" => Case(_formatterSerializer, new ListOfAddressMessage(
            new List<Address> { Local, NoHost })),

        "formatter-element-nullable-address-list" => Case(_formatterSerializer, new NullableAddressElementsMessage(
            new List<Address?> { null, Remote })),

        "formatter-key-and-value-address-dictionary" => Case(_formatterSerializer, new AddressToAddressMessage(
            new Dictionary<Address, Address> { [Local] = Remote })),

        // One snapshot for every collection shape with a formatter-handled member: single-element sets and
        // dictionaries only, since multi-element hash iteration order is not stable (see README.md).
        "formatter-all-address-shapes-single-element" => Case(_formatterSerializer, new AddressPositions(
            ListOf: new List<Address> { Local },
            ArrayOf: new[] { Remote },
            ImmutableArrayOf: ImmutableArray.Create(NoHost),
            ReadOnlyListOf: new List<Address> { Local },
            ReadOnlyCollectionOf: new List<Address> { Remote },
            ImmutableListOf: ImmutableList.Create(NoHost),
            ImmutableSetOf: ImmutableHashSet.Create(Local),
            ByKey: new Dictionary<Address, long> { [Local] = 1L },
            ReadOnlyByKey: new Dictionary<Address, string> { [Remote] = "r" },
            ImmutableByKey: ImmutableDictionary<Address, int>.Empty.Add(NoHost, 2),
            ByValue: new Dictionary<string, Address> { ["k"] = Local },
            ReadOnlyByValue: new Dictionary<string, Address> { ["k"] = Remote },
            ImmutableByValue: ImmutableDictionary<string, Address>.Empty.Add("k", NoHost),
            KeyAndValue: new Dictionary<Address, Address> { [Local] = Remote },
            NullableElements: new List<Address?> { null },
            Nested: new List<List<Address>> { new() { Local } },
            MapOfLists: new Dictionary<string, List<Address>> { ["k"] = new() { Remote } })),

        "formatter-custom-reference-type-elements" => Case(_formatterSerializer, new TagPositions(
            ListOf: new List<PositionTag> { new("a"), new("b") },
            ArrayOf: new[] { new PositionTag("c") },
            ByKey: new Dictionary<PositionTag, int> { [new PositionTag("k")] = 7 },
            ByValue: new Dictionary<int, PositionTag> { [1] = new PositionTag("v") },
            KeyAndValue: new Dictionary<PositionTag, PositionTag> { [new PositionTag("kk")] = new PositionTag("vv") },
            ImmutableSetOf: ImmutableHashSet.Create(new PositionTag("s")))),

        "formatter-custom-value-type-nullable-elements" => Case(_formatterSerializer, new CelsiusPositions(
            ListOf: new List<PositionCelsius> { new(-273.15), new(0) },
            ArrayOf: new[] { new PositionCelsius(1.5) },
            NullableListOf: new List<PositionCelsius?> { new PositionCelsius(2), null },
            ImmutableArrayOfNullable: ImmutableArray.Create<PositionCelsius?>(null, new PositionCelsius(4)),
            ByKey: new Dictionary<PositionCelsius, string> { [new PositionCelsius(5)] = "five" },
            ByNullableValue: new Dictionary<string, PositionCelsius?> { ["none"] = null })),

        // The IActorRef formatter registered on the serializer wins over the native path-string encoding,
        // at element, key and value position alike.
        "formatter-actor-ref-elements-override-native" => Case(_formatterSerializer, new RefPositions(
            ListOf: new List<IActorRef> { _system.DeadLetters },
            ReadOnlyListOf: new List<IActorRef> { _system.DeadLetters },
            ByValue: new Dictionary<string, IActorRef> { ["dl"] = _system.DeadLetters },
            ByKey: new Dictionary<IActorRef, int> { [_system.DeadLetters] = 1 },
            ImmutableArrayOf: ImmutableArray.Create(_system.DeadLetters))),

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
