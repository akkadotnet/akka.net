//-----------------------------------------------------------------------
// <copyright file="WireFormatSnapshotScalarSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit;
using VerifyXunit;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// COMMITTED WIRE-FORMAT SNAPSHOTS for the native <c>TimeSpan</c>, <c>float</c>, <c>short</c>, <c>byte</c>,
/// <c>sbyte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c> and <c>char</c> (G-3), as fields, Nullable fields and
/// collection members. Same mechanism and update procedure as <see cref="WireFormatSnapshotSpec"/> (see
/// <c>WireSnapshots/README.md</c>); a separate spec so those existing cases, and their snapshots, stay
/// untouched. Every input is a hardcoded constant, so a mismatch always means the wire format changed.
/// </summary>
public sealed class WireFormatSnapshotScalarSpec : IAsyncLifetime
{
    private static readonly string[] AllCaseNames =
    {
        // G-3: native TimeSpan, float, short, byte, sbyte, ushort, uint, ulong, char.
        "scalars-min",
        "scalars-max",
        "scalars-zero",
        "scalars-nullable-all-null",
        "scalars-nullable-populated",
        "scalars-in-every-collection-shape-single-element",
    };

    private ActorSystem _system = null!;
    private NativeScalarSerializer _scalarSerializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("wire-format-snapshot-scalar");
        var extendedSystem = (ExtendedActorSystem)_system;
        _scalarSerializer = new NativeScalarSerializer(extendedSystem);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    public static IEnumerable<object[]> CaseNames() => AllCaseNames.Select(name => new object[] { name });

    [Theory(DisplayName = "Should_MatchCommittedSnapshot_When_NativeScalarShapesAreSerialized")]
    [MemberData(nameof(CaseNames))]
    public Task Should_MatchCommittedSnapshot_When_NativeScalarShapesAreSerialized(string caseName)
    {
        var wireCase = BuildCase(caseName);
        var hexDump = HexDumpFormatter.Format(caseName, wireCase.MessageType, wireCase.Manifest, wireCase.SerializerId, wireCase.Bytes);

        return Verifier.Verify(hexDump)
            .UseDirectory("WireSnapshots")
            .UseFileName(caseName);
    }

    private WireSnapshotCase BuildCase(string caseName) => caseName switch
    {
        "scalars-min" => Case(_scalarSerializer, new SmallScalarsMessage(
            TimeSpan.MinValue, float.MinValue, short.MinValue, byte.MinValue, sbyte.MinValue, ushort.MinValue, uint.MinValue, ulong.MinValue, char.MinValue)),

        "scalars-max" => Case(_scalarSerializer, new SmallScalarsMessage(
            TimeSpan.MaxValue, float.MaxValue, short.MaxValue, byte.MaxValue, sbyte.MaxValue, ushort.MaxValue, uint.MaxValue, ulong.MaxValue, char.MaxValue)),

        "scalars-zero" => Case(_scalarSerializer, new SmallScalarsMessage(
            TimeSpan.Zero, 0f, 0, 0, 0, 0, 0u, 0ul, '\0')),

        "scalars-nullable-all-null" => Case(_scalarSerializer, new NullableScalarsMessage(
            null, null, null, null, null, null, null, null, null)),

        "scalars-nullable-populated" => Case(_scalarSerializer, new NullableScalarsMessage(
            TimeSpan.FromTicks(300), 1.5f, -200, 200, -5, 40000, 3000000000u, ulong.MaxValue, 'A')),

        "scalars-in-every-collection-shape-single-element" => Case(_scalarSerializer, new ScalarCollectionsMessage(
            Spans: new List<TimeSpan> { TimeSpan.FromMilliseconds(250) },
            Floats: new[] { -0.5f },
            NullableShorts: new List<short?> { null, 300 },
            Bytes: ImmutableArray.Create<byte>(255),
            SBytes: new List<sbyte> { -100 },
            UShorts: ImmutableList.Create<ushort>(40000),
            NullableUInts: new List<uint?> { uint.MaxValue },
            ULongs: new[] { ulong.MaxValue },
            Chars: ImmutableHashSet.Create('x'),
            SpanToULong: new Dictionary<TimeSpan, ulong> { [TimeSpan.FromTicks(1)] = 2ul },
            CharToUInt: new Dictionary<char, uint> { ['y'] = 70000u },
            ShortToFloat: ImmutableDictionary<short, float>.Empty.Add(-3, 2.5f),
            NullableSpanByName: new Dictionary<string, TimeSpan?> { ["none"] = null },
            NullableSpans: new List<TimeSpan?> { null, TimeSpan.FromTicks(-5) },
            ByteToNullableSByte: new Dictionary<byte, sbyte?> { [9] = null })),

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
