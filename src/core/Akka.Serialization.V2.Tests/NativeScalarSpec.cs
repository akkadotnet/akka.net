//-----------------------------------------------------------------------
// <copyright file="NativeScalarSpec.cs" company="Akka.NET Project">
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
/// Native support for <see cref="TimeSpan"/>, <see cref="float"/>, <see cref="short"/>, <see cref="byte"/>,
/// <see cref="sbyte"/>, <see cref="ushort"/>, <see cref="uint"/>, <see cref="ulong"/> and <see cref="char"/> (G-3),
/// as fields, as Nullable fields, and as collection elements, dictionary keys and dictionary values.
/// </summary>
/// <remarks>
/// Wire encodings under test: TimeSpan is the MessagePack integer of its Ticks (int64); float is MessagePack
/// float32; the integer types use MessagePack's smallest integer encoding (as int and long already do); char
/// is the unsigned integer of its UTF-16 code unit. Reads range-check and throw instead of truncating.
/// </remarks>
public sealed class NativeScalarSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private NativeScalarSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("native-scalar-spec");
        _serializer = new NativeScalarSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    private static SmallScalarsMessage ScalarCase(string caseName) => caseName switch
    {
        "min" => new SmallScalarsMessage(TimeSpan.MinValue, float.MinValue, short.MinValue, byte.MinValue, sbyte.MinValue, ushort.MinValue, uint.MinValue, ulong.MinValue, char.MinValue),
        "max" => new SmallScalarsMessage(TimeSpan.MaxValue, float.MaxValue, short.MaxValue, byte.MaxValue, sbyte.MaxValue, ushort.MaxValue, uint.MaxValue, ulong.MaxValue, char.MaxValue),
        "zero" => new SmallScalarsMessage(TimeSpan.Zero, 0f, 0, 0, 0, 0, 0u, 0ul, '\0'),
        "negative-and-fractional" => new SmallScalarsMessage(TimeSpan.FromTicks(-1), -1.5f, -1, 1, -1, 1, 1u, 1ul, 'A'),
        "encoding-boundaries" => new SmallScalarsMessage(TimeSpan.FromTicks(128), float.Epsilon, -33, 128, -128, 256, 65536u, 4294967296ul, '\uD800'),
        "float-nan" => new SmallScalarsMessage(TimeSpan.FromSeconds(30), float.NaN, 127, 127, 127, 127, 127u, 127ul, 'z'),
        "float-negative-infinity" => new SmallScalarsMessage(TimeSpan.FromDays(1), float.NegativeInfinity, -32, 255, -32, 65535, 255u, 255ul, '\uFFFE'),
        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown scalar case.")
    };

    [Theory(DisplayName = "Should_RoundTripEveryScalar_When_ValuesAreAtTheirLimits")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("zero")]
    [InlineData("negative-and-fractional")]
    [InlineData("encoding-boundaries")]
    [InlineData("float-nan")]
    [InlineData("float-negative-infinity")]
    public void Should_RoundTripEveryScalar_When_ValuesAreAtTheirLimits(string caseName)
    {
        var message = ScalarCase(caseName);

        var recovered = RoundTrip(message);

        recovered.Span.Should().Be(message.Span, caseName);
        recovered.Single.Should().Be(message.Single, caseName);
        recovered.Int16.Should().Be(message.Int16, caseName);
        recovered.Byte.Should().Be(message.Byte, caseName);
        recovered.SByte.Should().Be(message.SByte, caseName);
        recovered.UInt16.Should().Be(message.UInt16, caseName);
        recovered.UInt32.Should().Be(message.UInt32, caseName);
        recovered.UInt64.Should().Be(message.UInt64, caseName);
        recovered.Char.Should().Be(message.Char, caseName);
    }

    [Theory(DisplayName = "Should_ReportExactSizeHint_When_ScalarsAreAtTheirLimits")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("zero")]
    [InlineData("negative-and-fractional")]
    [InlineData("encoding-boundaries")]
    [InlineData("float-nan")]
    [InlineData("float-negative-infinity")]
    public void Should_ReportExactSizeHint_When_ScalarsAreAtTheirLimits(string caseName)
    {
        var message = ScalarCase(caseName);

        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length, caseName);
    }

    [Fact(DisplayName = "Should_RoundTripNegativeZeroFloat_When_FloatIsNegativeZero")]
    public void Should_RoundTripNegativeZeroFloat_When_FloatIsNegativeZero()
    {
        var recovered = RoundTrip(new SmallScalarsMessage(TimeSpan.Zero, -0f, 0, 0, 0, 0, 0, 0, '\0'));

        float.IsNegative(recovered.Single).Should().BeTrue();
    }

    [Fact(DisplayName = "Should_WriteTheDocumentedEncoding_When_ScalarsAreSerialized")]
    public void Should_WriteTheDocumentedEncoding_When_ScalarsAreSerialized()
    {
        var message = new SmallScalarsMessage(
            TimeSpan.FromTicks(300), 1.5f, -200, 200, -5, 40000, 3000000000u, ulong.MaxValue, 'A');

        var expected = new byte[]
        {
            0x89, // map(9)
            0x01, 0xcd, 0x01, 0x2c, //  1 TimeSpan: Ticks 300 as uint16
            0x02, 0xca, 0x3f, 0xc0, 0x00, 0x00, //  2 float: float32 1.5
            0x03, 0xd1, 0xff, 0x38, //  3 short: int16 -200
            0x04, 0xcc, 0xc8, //  4 byte: uint8 200
            0x05, 0xfb, //  5 sbyte: negative fixint -5
            0x06, 0xcd, 0x9c, 0x40, //  6 ushort: uint16 40000
            0x07, 0xce, 0xb2, 0xd0, 0x5e, 0x00, //  7 uint: uint32 3000000000
            0x08, 0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, //  8 ulong: uint64 max
            0x09, 0x41 //  9 char: code unit 0x41 as positive fixint
        };

        _serializer.ToBinary(message).Should().Equal(expected);
    }

    // ------------------------------------------------------------------------------------------
    // Nullable variants
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Should_RoundTripNull_When_EveryNullableScalarIsNull")]
    public void Should_RoundTripNull_When_EveryNullableScalarIsNull()
    {
        var message = new NullableScalarsMessage(null, null, null, null, null, null, null, null, null);

        var recovered = RoundTrip(message);

        recovered.Should().Be(message);
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_RoundTripValues_When_EveryNullableScalarIsPopulatedAtItsLimits")]
    public void Should_RoundTripValues_When_EveryNullableScalarIsPopulatedAtItsLimits()
    {
        foreach (var message in new[]
                 {
                     new NullableScalarsMessage(TimeSpan.MinValue, float.MinValue, short.MinValue, byte.MinValue, sbyte.MinValue, ushort.MinValue, uint.MinValue, ulong.MinValue, char.MinValue),
                     new NullableScalarsMessage(TimeSpan.MaxValue, float.MaxValue, short.MaxValue, byte.MaxValue, sbyte.MaxValue, ushort.MaxValue, uint.MaxValue, ulong.MaxValue, char.MaxValue),
                     new NullableScalarsMessage(TimeSpan.Zero, 0f, 0, 0, 0, 0, 0u, 0ul, '\0')
                 })
        {
            RoundTrip(message).Should().Be(message);
            _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
        }
    }

    [Fact(DisplayName = "Should_WriteTheSameBytesAsNonNullable_When_ANullableScalarIsPresent")]
    public void Should_WriteTheSameBytesAsNonNullable_When_ANullableScalarIsPresent()
    {
        var populated = _serializer.ToBinary(new NullableScalarsMessage(
            TimeSpan.FromTicks(300), 1.5f, -200, 200, -5, 40000, 3000000000u, ulong.MaxValue, 'A'));
        var nonNullable = _serializer.ToBinary(new SmallScalarsMessage(
            TimeSpan.FromTicks(300), 1.5f, -200, 200, -5, 40000, 3000000000u, ulong.MaxValue, 'A'));

        // A present nullable value is written exactly as the non-nullable one: same map, same bytes.
        // (An absent one is nil; the scalars-nullable-all-null snapshot pins those bytes.)
        populated.Should().Equal(nonNullable);
    }

    // ------------------------------------------------------------------------------------------
    // Collections: element, key and value positions
    // ------------------------------------------------------------------------------------------

    private static ScalarCollectionsMessage PopulatedCollections() => new(
        Spans: new List<TimeSpan> { TimeSpan.MinValue, TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.MaxValue },
        Floats: new[] { float.MinValue, -0.5f, 0f, float.MaxValue },
        NullableShorts: new List<short?> { short.MinValue, null, 0, short.MaxValue },
        Bytes: ImmutableArray.Create<byte>(0, 1, 127, 128, 255),
        SBytes: new List<sbyte> { sbyte.MinValue, -1, 0, sbyte.MaxValue },
        UShorts: ImmutableList.Create<ushort>(0, 255, 256, ushort.MaxValue),
        NullableUInts: new List<uint?> { null, 0u, uint.MaxValue },
        ULongs: new[] { 0ul, 1ul, ulong.MaxValue },
        Chars: ImmutableHashSet.Create('a', '\0', char.MaxValue),
        SpanToULong: new Dictionary<TimeSpan, ulong> { [TimeSpan.Zero] = 0ul, [TimeSpan.FromTicks(1)] = ulong.MaxValue },
        CharToUInt: new Dictionary<char, uint> { ['x'] = 7u, [char.MaxValue] = uint.MaxValue },
        ShortToFloat: ImmutableDictionary<short, float>.Empty.Add(short.MinValue, float.MinValue).Add(5, 2.5f),
        NullableSpanByName: new Dictionary<string, TimeSpan?> { ["has"] = TimeSpan.FromHours(1), ["none"] = null },
        NullableSpans: new List<TimeSpan?> { null, TimeSpan.MinValue, null },
        ByteToNullableSByte: new Dictionary<byte, sbyte?> { [0] = null, [255] = sbyte.MinValue });

    [Fact(DisplayName = "Should_RoundTripEveryCollectionShape_When_ScalarsAreElementsKeysAndValues")]
    public void Should_RoundTripEveryCollectionShape_When_ScalarsAreElementsKeysAndValues()
    {
        var message = PopulatedCollections();

        var recovered = RoundTrip(message);

        recovered.Spans.Should().Equal(message.Spans);
        recovered.Floats.Should().Equal(message.Floats);
        recovered.NullableShorts.Should().Equal(message.NullableShorts);
        recovered.Bytes.Should().Equal(message.Bytes);
        recovered.SBytes.Should().Equal(message.SBytes);
        recovered.UShorts.Should().Equal(message.UShorts);
        recovered.NullableUInts.Should().Equal(message.NullableUInts);
        recovered.ULongs.Should().Equal(message.ULongs);
        recovered.Chars.Should().BeEquivalentTo(message.Chars);
        recovered.SpanToULong.Should().BeEquivalentTo(message.SpanToULong);
        recovered.CharToUInt.Should().BeEquivalentTo(message.CharToUInt);
        recovered.ShortToFloat.Should().BeEquivalentTo(message.ShortToFloat);
        recovered.NullableSpanByName.Should().BeEquivalentTo(message.NullableSpanByName);
        recovered.NullableSpans.Should().Equal(message.NullableSpans);
        recovered.ByteToNullableSByte.Should().BeEquivalentTo(message.ByteToNullableSByte);
    }

    [Fact(DisplayName = "Should_ReportExactSizeHint_When_ScalarsAreCollectionMembers")]
    public void Should_ReportExactSizeHint_When_ScalarsAreCollectionMembers()
    {
        var message = PopulatedCollections();

        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_RoundTripNullAndEmpty_When_ScalarCollectionsAreNullOrEmpty")]
    public void Should_RoundTripNullAndEmpty_When_ScalarCollectionsAreNullOrEmpty()
    {
        var allNull = new ScalarCollectionsMessage(null, null, null, default, null, null, null, null, null, null, null, null, null, null, null);
        var recoveredNull = RoundTrip(allNull);

        recoveredNull.Spans.Should().BeNull();
        recoveredNull.Bytes.IsDefault.Should().BeTrue();
        recoveredNull.SpanToULong.Should().BeNull();
        _serializer.SizeHint(allNull).Should().Be(_serializer.ToBinary(allNull).Length);

        var allEmpty = new ScalarCollectionsMessage(
            new List<TimeSpan>(), Array.Empty<float>(), new List<short?>(), ImmutableArray<byte>.Empty, new List<sbyte>(),
            ImmutableList<ushort>.Empty, new List<uint?>(), Array.Empty<ulong>(), ImmutableHashSet<char>.Empty,
            new Dictionary<TimeSpan, ulong>(), new Dictionary<char, uint>(), ImmutableDictionary<short, float>.Empty,
            new Dictionary<string, TimeSpan?>(), new List<TimeSpan?>(), new Dictionary<byte, sbyte?>());
        var recoveredEmpty = RoundTrip(allEmpty);

        recoveredEmpty.Spans.Should().NotBeNull().And.BeEmpty();
        recoveredEmpty.Bytes.IsDefault.Should().BeFalse();
        recoveredEmpty.SpanToULong.Should().NotBeNull().And.BeEmpty();
        _serializer.SizeHint(allEmpty).Should().Be(_serializer.ToBinary(allEmpty).Length);
    }

    [Fact(DisplayName = "Should_WriteBareEncodedElements_When_ScalarsAreInAList")]
    public void Should_WriteBareEncodedElements_When_ScalarsAreInAList()
    {
        var message = new SpanListMessage(new List<TimeSpan?> { TimeSpan.FromTicks(5), null, TimeSpan.FromTicks(-5) });

        // map(1) { 1: array(3) [ int 5, nil, int -5 ] } -- TimeSpan is its Ticks, no wrapper.
        _serializer.ToBinary(message).Should().Equal(0x81, 0x01, 0x93, 0x05, 0xc0, 0xfb);
    }

    // ------------------------------------------------------------------------------------------
    // Reads range-check: a value the target type cannot hold throws, it never truncates
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Where each scalar type can be hand-fed a bad wire value: its single-field message, the field index in
    /// <see cref="NullableScalarsMessage"/>, the field index of a collection that holds it as a list-like
    /// element in <see cref="ScalarCollectionsMessage"/>, and (for three types) a dictionary whose VALUE it is.
    /// </summary>
    private sealed record ScalarSpec(string TypeName, string FieldManifest, int NullableIndex, int ElementIndex, int MapValueIndex = 0, object? MapKey = null);

    private static readonly ScalarSpec[] Specs =
    {
        new("System.Int16", "int16-v1", 3, 3),
        new("System.SByte", "sbyte-v1", 5, 5, MapValueIndex: 15, MapKey: 0L),
        new("System.Byte", "byte-v1", 4, 4),
        new("System.UInt16", "uint16-v1", 6, 6),
        new("System.UInt32", "uint32-v1", 7, 7),
        new("System.UInt64", "uint64-v1", 8, 8, MapValueIndex: 10, MapKey: 0L),
        new("System.Char", "char-v1", 9, 9),
        new("System.TimeSpan", "timespan-v1", 1, 1, MapValueIndex: 13, MapKey: "k"),
        new("System.Single", "single-v1", 2, 2),
    };

    private static IEnumerable<object> BadWireValues(string typeName) => typeName switch
    {
        "System.Int16" => new object[] { 32768L, -32769L, ulong.MaxValue, "text" },
        "System.SByte" => new object[] { 128L, -129L, ulong.MaxValue, "text" },
        "System.Byte" => new object[] { 256L, -1L, ulong.MaxValue, "text" },
        "System.UInt16" => new object[] { 65536L, -1L, ulong.MaxValue, "text" },
        "System.UInt32" => new object[] { 4294967296L, -1L, ulong.MaxValue, "text" },
        "System.UInt64" => new object[] { -1L, long.MinValue, "text" },
        "System.Char" => new object[] { 65536L, -1L, ulong.MaxValue, "text" },
        "System.TimeSpan" => new object[] { ulong.MaxValue, "text" },
        "System.Single" => new object[] { 1e300d, -1e300d, "text" },
        _ => throw new ArgumentOutOfRangeException(nameof(typeName), typeName, null)
    };

    public static IEnumerable<object[]> RangeCases()
    {
        foreach (var spec in Specs)
        {
            foreach (var value in BadWireValues(spec.TypeName))
            {
                yield return new[] { spec.TypeName, "field", value };
                yield return new[] { spec.TypeName, "nullable", value };
                yield return new[] { spec.TypeName, "element", value };
                if (spec.MapValueIndex != 0)
                    yield return new[] { spec.TypeName, "map-value", value };
            }
        }
    }

    private static void WriteWire(ref MessagePackWriter writer, object value)
    {
        switch (value)
        {
            case long signed:
                writer.Write(signed);
                break;
            case ulong unsigned:
                writer.Write(unsigned);
                break;
            case double floating:
                writer.Write(floating);
                break;
            default:
                writer.Write((string)value);
                break;
        }
    }

    [Theory(DisplayName = "Should_ThrowNamingTheType_When_WireValueCannotBeReadAsTheScalarType")]
    [MemberData(nameof(RangeCases))]
    public void Should_ThrowNamingTheType_When_WireValueCannotBeReadAsTheScalarType(string typeName, string position, object wireValue)
    {
        var spec = Specs.Single(candidate => candidate.TypeName == typeName);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        string manifest;
        writer.WriteMapHeader(1);
        switch (position)
        {
            case "field":
                manifest = spec.FieldManifest;
                writer.Write(1);
                WriteWire(ref writer, wireValue);
                break;
            case "nullable":
                manifest = "nullable-scalars-v1";
                writer.Write(spec.NullableIndex);
                WriteWire(ref writer, wireValue);
                break;
            case "element":
                manifest = "scalar-collections-v1";
                writer.Write(spec.ElementIndex);
                writer.WriteArrayHeader(1);
                WriteWire(ref writer, wireValue);
                break;
            default:
                manifest = "scalar-collections-v1";
                writer.Write(spec.MapValueIndex);
                writer.WriteMapHeader(1);
                WriteWire(ref writer, spec.MapKey!);
                WriteWire(ref writer, wireValue);
                break;
        }

        writer.Flush();

        Action read = () => _serializer.FromBinary(buffer.WrittenMemory.ToArray(), manifest);

        read.Should().Throw<MessagePackSerializationException>($"{typeName} at {position}").WithMessage($"*[{typeName}]*");
    }

    [Theory(DisplayName = "Should_ReadTheValue_When_WireIntegerWasWrittenWithAWiderOrNarrowerWidth")]
    [InlineData(100)]
    [InlineData(-100)]
    [InlineData(short.MaxValue)]
    [InlineData(short.MinValue)]
    public void Should_ReadTheValue_When_WireIntegerWasWrittenWithAWiderOrNarrowerWidth(int value)
    {
        // A peer may legally write the same integer with any width (for example a fixed int64): reading
        // is by value, not by encoding.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.WriteInt64((long)value);
        writer.Flush();

        var recovered = (ScalarOnlyInt16)_serializer.FromBinary(buffer.WrittenMemory.ToArray(), "int16-v1");

        recovered.Value.Should().Be((short)value);
    }

    [Fact(DisplayName = "Should_ReadFloat32_When_WireHoldsAFloat64ThatFits")]
    public void Should_ReadFloat32_When_WireHoldsAFloat64ThatFits()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.Write(1.5d);
        writer.Flush();

        var recovered = (ScalarOnlySingle)_serializer.FromBinary(buffer.WrittenMemory.ToArray(), "single-v1");

        recovered.Value.Should().Be(1.5f);
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, INativeScalarProtocol
    {
        var bytes = _serializer.ToBinary(message);
        return _serializer.FromBinary(bytes, _serializer.Manifest(message)).Should().BeOfType<TMessage>().Subject;
    }
}

// ----------------------------------------------------------------------------------------------
// Fixtures
// ----------------------------------------------------------------------------------------------

public interface INativeScalarProtocol
{
}

[AkkaSerializer<INativeScalarProtocol>("native-scalar-test", 122003)]
public sealed partial class NativeScalarSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "small-scalars-v1")]
public sealed record SmallScalarsMessage(
    [property: AkkaField(1)] TimeSpan Span,
    [property: AkkaField(2)] float Single,
    [property: AkkaField(3)] short Int16,
    [property: AkkaField(4)] byte Byte,
    [property: AkkaField(5)] sbyte SByte,
    [property: AkkaField(6)] ushort UInt16,
    [property: AkkaField(7)] uint UInt32,
    [property: AkkaField(8)] ulong UInt64,
    [property: AkkaField(9)] char Char) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "nullable-scalars-v1")]
public sealed record NullableScalarsMessage(
    [property: AkkaField(1)] TimeSpan? Span,
    [property: AkkaField(2)] float? Single,
    [property: AkkaField(3)] short? Int16,
    [property: AkkaField(4)] byte? Byte,
    [property: AkkaField(5)] sbyte? SByte,
    [property: AkkaField(6)] ushort? UInt16,
    [property: AkkaField(7)] uint? UInt32,
    [property: AkkaField(8)] ulong? UInt64,
    [property: AkkaField(9)] char? Char) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "scalar-collections-v1")]
public sealed record ScalarCollectionsMessage(
    [property: AkkaField(1)] List<TimeSpan>? Spans,
    [property: AkkaField(2)] float[]? Floats,
    [property: AkkaField(3)] List<short?>? NullableShorts,
    [property: AkkaField(4)] ImmutableArray<byte> Bytes,
    [property: AkkaField(5)] IReadOnlyList<sbyte>? SBytes,
    [property: AkkaField(6)] ImmutableList<ushort>? UShorts,
    [property: AkkaField(7)] IReadOnlyCollection<uint?>? NullableUInts,
    [property: AkkaField(8)] ulong[]? ULongs,
    [property: AkkaField(9)] ImmutableHashSet<char>? Chars,
    [property: AkkaField(10)] Dictionary<TimeSpan, ulong>? SpanToULong,
    [property: AkkaField(11)] Dictionary<char, uint>? CharToUInt,
    [property: AkkaField(12)] ImmutableDictionary<short, float>? ShortToFloat,
    [property: AkkaField(13)] IReadOnlyDictionary<string, TimeSpan?>? NullableSpanByName,
    [property: AkkaField(14)] List<TimeSpan?>? NullableSpans,
    [property: AkkaField(15)] Dictionary<byte, sbyte?>? ByteToNullableSByte) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "span-list-v1")]
public sealed record SpanListMessage(
    [property: AkkaField(1)] List<TimeSpan?> Spans) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "int16-v1")]
public sealed record ScalarOnlyInt16([property: AkkaField(1)] short Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "sbyte-v1")]
public sealed record ScalarOnlySByte([property: AkkaField(1)] sbyte Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "byte-v1")]
public sealed record ScalarOnlyByte([property: AkkaField(1)] byte Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "uint16-v1")]
public sealed record ScalarOnlyUInt16([property: AkkaField(1)] ushort Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "uint32-v1")]
public sealed record ScalarOnlyUInt32([property: AkkaField(1)] uint Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "uint64-v1")]
public sealed record ScalarOnlyUInt64([property: AkkaField(1)] ulong Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "char-v1")]
public sealed record ScalarOnlyChar([property: AkkaField(1)] char Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "single-v1")]
public sealed record ScalarOnlySingle([property: AkkaField(1)] float Value) : INativeScalarProtocol;

[AkkaSerializable(Manifest = "timespan-v1")]
public sealed record ScalarOnlyTimeSpan([property: AkkaField(1)] TimeSpan Value) : INativeScalarProtocol;
