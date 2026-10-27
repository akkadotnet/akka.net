//-----------------------------------------------------------------------
// <copyright file="FormatterCollectionSpec.cs" company="Akka.NET Project">
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
/// Formatter resolution at collection ELEMENT, dictionary KEY and dictionary VALUE position (G-1):
/// every collection shape the generator supports holds a formatter-handled type (built-in
/// <see cref="Address"/>, a custom reference-type formatter, a custom value-type formatter, and a custom
/// <see cref="IActorRef"/> formatter that overrides the native encoding) exactly as a field does.
/// The wire contract under test: an array or map whose elements are exactly what the formatter writes at
/// field position, with no wrapper.
/// </summary>
public sealed class FormatterCollectionSpec : IAsyncLifetime
{
    private static readonly Address Local = new("akka", "sys", "localhost", 2552);
    private static readonly Address Remote = new("akka.tcp", "other", "10.0.0.2", 4053);
    private static readonly Address NoHost = new("akka", "sys");

    private ActorSystem _system = null!;
    private FormatterPositionSerializer _serializer = null!;
    private NativeRefPositionSerializer _nativeRefSerializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("formatter-collection-spec");
        _serializer = new FormatterPositionSerializer((ExtendedActorSystem)_system);
        _nativeRefSerializer = new NativeRefPositionSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    // ------------------------------------------------------------------------------------------
    // Address (built-in formatter, reference type) in every position of every collection shape
    // ------------------------------------------------------------------------------------------

    private static AddressPositions PopulatedAddressPositions() => new(
        ListOf: new List<Address> { Local, Remote, NoHost },
        ArrayOf: new[] { Remote, Local },
        ImmutableArrayOf: ImmutableArray.Create(Local, NoHost),
        ReadOnlyListOf: new List<Address> { NoHost, Remote },
        ReadOnlyCollectionOf: new List<Address> { Local },
        ImmutableListOf: ImmutableList.Create(Remote, Local),
        ImmutableSetOf: ImmutableHashSet.Create(Local, Remote, NoHost),
        ByKey: new Dictionary<Address, long> { [Local] = long.MaxValue, [Remote] = long.MinValue, [NoHost] = 0 },
        ReadOnlyByKey: new Dictionary<Address, string> { [Local] = "local", [Remote] = "remote" },
        ImmutableByKey: ImmutableDictionary<Address, int>.Empty.Add(Local, 1).Add(NoHost, 2),
        ByValue: new Dictionary<string, Address> { ["a"] = Local, ["b"] = Remote },
        ReadOnlyByValue: new Dictionary<string, Address> { ["c"] = NoHost },
        ImmutableByValue: ImmutableDictionary<string, Address>.Empty.Add("d", Remote),
        KeyAndValue: new Dictionary<Address, Address> { [Local] = Remote, [Remote] = NoHost },
        NullableElements: new List<Address?> { Local, null, Remote, null },
        Nested: new List<List<Address>> { new() { Local }, new(), new() { Remote, NoHost } },
        MapOfLists: new Dictionary<string, List<Address>> { ["x"] = new() { Local, Remote }, ["y"] = new() });

    [Fact(DisplayName = "Should_RoundTripEveryShape_When_AddressIsFormattedInElementKeyAndValuePosition")]
    public void Should_RoundTripEveryShape_When_AddressIsFormattedInElementKeyAndValuePosition()
    {
        var recovered = RoundTrip(PopulatedAddressPositions());

        recovered.ListOf.Should().Equal(Local, Remote, NoHost);
        recovered.ArrayOf.Should().Equal(Remote, Local);
        recovered.ImmutableArrayOf.Should().Equal(Local, NoHost);
        recovered.ReadOnlyListOf.Should().Equal(NoHost, Remote);
        recovered.ReadOnlyCollectionOf.Should().Equal(Local);
        recovered.ImmutableListOf.Should().Equal(Remote, Local);
        recovered.ImmutableSetOf.Should().BeEquivalentTo(new[] { Local, Remote, NoHost });
        recovered.ByKey.Should().BeEquivalentTo(new Dictionary<Address, long> { [Local] = long.MaxValue, [Remote] = long.MinValue, [NoHost] = 0 });
        recovered.ReadOnlyByKey.Should().BeEquivalentTo(new Dictionary<Address, string> { [Local] = "local", [Remote] = "remote" });
        recovered.ImmutableByKey.Should().BeEquivalentTo(new Dictionary<Address, int> { [Local] = 1, [NoHost] = 2 });
        recovered.ByValue.Should().BeEquivalentTo(new Dictionary<string, Address> { ["a"] = Local, ["b"] = Remote });
        recovered.ReadOnlyByValue.Should().BeEquivalentTo(new Dictionary<string, Address> { ["c"] = NoHost });
        recovered.ImmutableByValue.Should().BeEquivalentTo(new Dictionary<string, Address> { ["d"] = Remote });
        recovered.KeyAndValue.Should().BeEquivalentTo(new Dictionary<Address, Address> { [Local] = Remote, [Remote] = NoHost });
        recovered.NullableElements.Should().Equal(Local, null, Remote, null);
        recovered.Nested!.Select(inner => inner.ToArray()).Should().BeEquivalentTo(new[]
        {
            new[] { Local }, Array.Empty<Address>(), new[] { Remote, NoHost }
        }, options => options.WithStrictOrdering());
        recovered.MapOfLists!["x"].Should().Equal(Local, Remote);
        recovered.MapOfLists["y"].Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_ReportExactSizeHint_When_ElementsKeysAndValuesAreFormatted")]
    public void Should_ReportExactSizeHint_When_ElementsKeysAndValuesAreFormatted()
    {
        var message = PopulatedAddressPositions();

        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_RoundTripNullAndEmpty_When_FormattedCollectionFieldsAreNullOrEmpty")]
    public void Should_RoundTripNullAndEmpty_When_FormattedCollectionFieldsAreNullOrEmpty()
    {
        var allNull = new AddressPositions(null, null, default, null, null, null, null, null, null, null, null, null, null, null, new List<Address?>(), null, null);
        var recoveredNull = RoundTrip(allNull);

        recoveredNull.ListOf.Should().BeNull();
        recoveredNull.ArrayOf.Should().BeNull();
        recoveredNull.ImmutableArrayOf.IsDefault.Should().BeTrue();
        recoveredNull.ByKey.Should().BeNull();
        recoveredNull.ImmutableByValue.Should().BeNull();
        _serializer.SizeHint(allNull).Should().Be(_serializer.ToBinary(allNull).Length);

        var allEmpty = new AddressPositions(
            new List<Address>(), Array.Empty<Address>(), ImmutableArray<Address>.Empty, new List<Address>(), new List<Address>(),
            ImmutableList<Address>.Empty, ImmutableHashSet<Address>.Empty, new Dictionary<Address, long>(), new Dictionary<Address, string>(),
            ImmutableDictionary<Address, int>.Empty, new Dictionary<string, Address>(), new Dictionary<string, Address>(),
            ImmutableDictionary<string, Address>.Empty, new Dictionary<Address, Address>(), new List<Address?>(),
            new List<List<Address>>(), new Dictionary<string, List<Address>>());
        var recoveredEmpty = RoundTrip(allEmpty);

        recoveredEmpty.ListOf.Should().NotBeNull().And.BeEmpty();
        recoveredEmpty.ImmutableArrayOf.IsDefault.Should().BeFalse();
        recoveredEmpty.ImmutableArrayOf.Should().BeEmpty();
        recoveredEmpty.ByKey.Should().NotBeNull().And.BeEmpty();
        recoveredEmpty.ImmutableByValue.Should().NotBeNull().And.BeEmpty();
        _serializer.SizeHint(allEmpty).Should().Be(_serializer.ToBinary(allEmpty).Length);
    }

    // ------------------------------------------------------------------------------------------
    // Wire contract: elements are exactly what the formatter writes at field position
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Should_WriteFormatterBytesAsArrayElements_When_ListOfAddress")]
    public void Should_WriteFormatterBytesAsArrayElements_When_ListOfAddress()
    {
        var message = new ListOfAddressMessage(new List<Address> { Local, NoHost });

        var expected = WriteBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(1);
            writer.Write(1);
            writer.WriteArrayHeader(2);
            new AddressFormatter().Write(ref writer, Local);
            new AddressFormatter().Write(ref writer, NoHost);
        });

        _serializer.ToBinary(message).Should().Equal(expected);
    }

    [Fact(DisplayName = "Should_WriteSameBytesAsFieldPosition_When_FormattedTypeIsAFieldAndAnElement")]
    public void Should_WriteSameBytesAsFieldPosition_When_FormattedTypeIsAFieldAndAnElement()
    {
        var asField = _serializer.ToBinary(new SingleAddressMessage(Remote));
        var asElement = _serializer.ToBinary(new ListOfAddressMessage(new List<Address> { Remote }));

        // Field position: map(1) { 1: <address> }. Element position: map(1) { 1: array(1) [<address>] }.
        // The address bytes are identical; the only difference is the one-byte array header.
        asElement.Should().Equal(asField.Take(2).Concat(new byte[] { 0x91 }).Concat(asField.Skip(2)));
    }

    // ------------------------------------------------------------------------------------------
    // Custom formatters: a reference type, a value type (plain and Nullable), and IActorRef
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Should_RoundTripReferenceFormatter_When_UsedAsElementKeyAndValue")]
    public void Should_RoundTripReferenceFormatter_When_UsedAsElementKeyAndValue()
    {
        var message = new TagPositions(
            ListOf: new List<PositionTag> { new("a"), new("b") },
            ArrayOf: new[] { new PositionTag("c") },
            ByKey: new Dictionary<PositionTag, int> { [new PositionTag("k")] = 7 },
            ByValue: new Dictionary<int, PositionTag> { [1] = new PositionTag("v") },
            KeyAndValue: new Dictionary<PositionTag, PositionTag> { [new PositionTag("kk")] = new PositionTag("vv") },
            ImmutableSetOf: ImmutableHashSet.Create(new PositionTag("s1"), new PositionTag("s2")));

        var recovered = RoundTrip(message);

        recovered.ListOf.Should().Equal(new PositionTag("a"), new PositionTag("b"));
        recovered.ArrayOf.Should().Equal(new PositionTag("c"));
        recovered.ByKey.Should().BeEquivalentTo(new Dictionary<PositionTag, int> { [new PositionTag("k")] = 7 });
        recovered.ByValue.Should().BeEquivalentTo(new Dictionary<int, PositionTag> { [1] = new PositionTag("v") });
        recovered.KeyAndValue.Should().BeEquivalentTo(new Dictionary<PositionTag, PositionTag> { [new PositionTag("kk")] = new PositionTag("vv") });
        recovered.ImmutableSetOf.Should().BeEquivalentTo(new[] { new PositionTag("s1"), new PositionTag("s2") });
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_RoundTripValueTypeFormatter_When_UsedAsElementKeyAndNullableValue")]
    public void Should_RoundTripValueTypeFormatter_When_UsedAsElementKeyAndNullableValue()
    {
        var message = new CelsiusPositions(
            ListOf: new List<PositionCelsius> { new(-273.15), new(0), new(100.5) },
            ArrayOf: new[] { new PositionCelsius(1) },
            NullableListOf: new List<PositionCelsius?> { new PositionCelsius(2), null, new PositionCelsius(3) },
            ImmutableArrayOfNullable: ImmutableArray.Create<PositionCelsius?>(null, new PositionCelsius(4)),
            ByKey: new Dictionary<PositionCelsius, string> { [new PositionCelsius(5)] = "five" },
            ByNullableValue: new Dictionary<string, PositionCelsius?> { ["has"] = new PositionCelsius(6), ["none"] = null });

        var recovered = RoundTrip(message);

        recovered.ListOf.Should().Equal(new PositionCelsius(-273.15), new PositionCelsius(0), new PositionCelsius(100.5));
        recovered.ArrayOf.Should().Equal(new PositionCelsius(1));
        recovered.NullableListOf.Should().Equal(new PositionCelsius(2), null, new PositionCelsius(3));
        recovered.ImmutableArrayOfNullable.Should().Equal(null, new PositionCelsius(4));
        recovered.ByKey.Should().BeEquivalentTo(new Dictionary<PositionCelsius, string> { [new PositionCelsius(5)] = "five" });
        recovered.ByNullableValue.Should().BeEquivalentTo(new Dictionary<string, PositionCelsius?> { ["has"] = new PositionCelsius(6), ["none"] = null });
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_RoundTripActorRefs_When_CustomFormatterOverridesNativeEncodingInCollections")]
    public void Should_RoundTripActorRefs_When_CustomFormatterOverridesNativeEncodingInCollections()
    {
        var worker = _system.ActorOf(Props.Create(() => new IdleActor()), "worker");
        var message = new RefPositions(
            ListOf: new List<IActorRef> { worker, _system.DeadLetters },
            ReadOnlyListOf: new List<IActorRef> { worker },
            ByValue: new Dictionary<string, IActorRef> { ["w"] = worker },
            ByKey: new Dictionary<IActorRef, int> { [worker] = 9 },
            ImmutableArrayOf: ImmutableArray.Create(worker, _system.DeadLetters));

        var recovered = RoundTrip(message);

        recovered.ListOf!.Select(r => r.Path).Should().Equal(worker.Path, _system.DeadLetters.Path);
        recovered.ReadOnlyListOf!.Single().Path.Should().Be(worker.Path);
        recovered.ByValue!["w"].Path.Should().Be(worker.Path);
        recovered.ByKey!.Keys.Single().Path.Should().Be(worker.Path);
        recovered.ImmutableArrayOf.Select(r => r.Path).Should().Equal(worker.Path, _system.DeadLetters.Path);
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Should_KeepNativeActorRefEncoding_When_NoActorRefFormatterIsRegistered")]
    public void Should_KeepNativeActorRefEncoding_When_NoActorRefFormatterIsRegistered()
    {
        var path = Akka.Serialization.Serialization.SerializedActorPath(_system.DeadLetters);
        var message = new NativeRefListMessage(new List<IActorRef> { _system.DeadLetters });

        var expected = WriteBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(1);
            writer.Write(1);
            writer.WriteArrayHeader(1);
            writer.Write(path); // native: a bare serialized-path string
        });

        _nativeRefSerializer.ToBinary(message).Should().Equal(expected);
        var recovered = (NativeRefListMessage)_nativeRefSerializer.FromBinary(_nativeRefSerializer.ToBinary(message), _nativeRefSerializer.Manifest(message));
        recovered.Refs.Single().Path.Should().Be(_system.DeadLetters.Path);
    }

    // ------------------------------------------------------------------------------------------
    // Built-in ActorPath formatter (needs the system) in collections
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Should_RoundTripActorPaths_When_BuiltInActorPathFormatterIsAnElementKeyAndValue")]
    public void Should_RoundTripActorPaths_When_BuiltInActorPathFormatterIsAnElementKeyAndValue()
    {
        var a = ActorPath.Parse($"akka://{_system.Name}/user/a");
        var b = ActorPath.Parse($"akka://{_system.Name}/user/b");
        var message = new ActorPathPositions(
            new List<ActorPath> { a, b },
            new Dictionary<ActorPath, ActorPath> { [a] = b });

        var recovered = RoundTrip(message);

        recovered.ListOf.Should().Equal(a, b);
        recovered.KeyAndValue.Should().BeEquivalentTo(new Dictionary<ActorPath, ActorPath> { [a] = b });
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    // ------------------------------------------------------------------------------------------
    // Native collections keep working next to formatted ones
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Should_KeepNativeEncoding_When_ANativeCollectionSharesAMessageWithAFormattedOne")]
    public void Should_KeepNativeEncoding_When_ANativeCollectionSharesAMessageWithAFormattedOne()
    {
        var message = new MixedMessage(new List<string> { "x", "y" }, new List<Address> { Local });

        var expected = WriteBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(2);
            writer.Write(1);
            writer.WriteArrayHeader(2);
            writer.Write("x");
            writer.Write("y");
            writer.Write(2);
            writer.WriteArrayHeader(1);
            new AddressFormatter().Write(ref writer, Local);
        });

        _serializer.ToBinary(message).Should().Equal(expected);
        RoundTrip(message).Natives.Should().Equal("x", "y");
    }

    [Fact(DisplayName = "Should_ReportUnknownSizeAndStillRoundTrip_When_AFormatterCannotSizeAnElementKeyOrValue")]
    public void Should_ReportUnknownSizeAndStillRoundTrip_When_AFormatterCannotSizeAnElementKeyOrValue()
    {
        var element = new UnsizedElementMessage(new List<UnsizedTag> { new("a"), new("b") });
        var key = new UnsizedKeyMessage(new Dictionary<UnsizedTag, int> { [new UnsizedTag("k")] = 1 });
        var value = new UnsizedValueMessage(new Dictionary<int, UnsizedTag> { [1] = new UnsizedTag("v") });

        _serializer.SizeHint(element).Should().Be(Akka.Serialization.SerializerV2.UnknownSize);
        _serializer.SizeHint(key).Should().Be(Akka.Serialization.SerializerV2.UnknownSize);
        _serializer.SizeHint(value).Should().Be(Akka.Serialization.SerializerV2.UnknownSize);

        RoundTrip(element).Tags.Should().Equal(new UnsizedTag("a"), new UnsizedTag("b"));
        RoundTrip(key).Tags.Should().BeEquivalentTo(new Dictionary<UnsizedTag, int> { [new UnsizedTag("k")] = 1 });
        RoundTrip(value).Tags.Should().BeEquivalentTo(new Dictionary<int, UnsizedTag> { [1] = new UnsizedTag("v") });
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IFormatterPositionProtocol
    {
        var bytes = _serializer.ToBinary(message);
        return _serializer.FromBinary(bytes, _serializer.Manifest(message)).Should().BeOfType<TMessage>().Subject;
    }

    private static byte[] WriteBytes(WriteAction write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        write(ref writer);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private delegate void WriteAction(ref MessagePackWriter writer);
}

// ----------------------------------------------------------------------------------------------
// Fixtures
// ----------------------------------------------------------------------------------------------

public interface IFormatterPositionProtocol
{
}

public interface INativeRefProtocol
{
}

public sealed class IdleActor : ReceiveActor
{
}

/// <summary>A reference-type formatter target whose formatter writes a bare, tagged string.</summary>
public sealed record PositionTag(string Value);

public sealed class PositionTagFormatter : IAkkaMessagePackFormatter<PositionTag>
{
    public void Write(ref MessagePackWriter writer, PositionTag value) => writer.Write("tag:" + value.Value);

    public PositionTag Read(ref MessagePackReader reader)
    {
        var raw = reader.ReadString() ?? throw new MessagePackSerializationException("Missing tag.");
        return new PositionTag(raw.Substring("tag:".Length));
    }

    public int SizeOf(PositionTag value) => MessagePackSizes.SizeOfString("tag:" + value.Value);
}

/// <summary>A value-type formatter target (constructed with the system) whose formatter writes a bare float64.</summary>
public readonly record struct PositionCelsius(double Degrees);

public sealed class PositionCelsiusFormatter : IAkkaMessagePackFormatter<PositionCelsius>
{
    public PositionCelsiusFormatter(ExtendedActorSystem system)
    {
    }

    public void Write(ref MessagePackWriter writer, PositionCelsius value) => writer.Write(value.Degrees);

    public PositionCelsius Read(ref MessagePackReader reader) => new(reader.ReadDouble());

    public int SizeOf(PositionCelsius value) => MessagePackSizes.SizeOfDouble(value.Degrees);
}

/// <summary>An IActorRef formatter with its own wire form, so a test can tell it from the native path-string encoding.</summary>
public sealed class PositionRefFormatter : IAkkaMessagePackFormatter<IActorRef>
{
    private readonly ExtendedActorSystem _system;

    public PositionRefFormatter(ExtendedActorSystem system)
    {
        _system = system;
    }

    public void Write(ref MessagePackWriter writer, IActorRef value)
    {
        writer.WriteArrayHeader(2);
        writer.Write("ref");
        writer.Write(Akka.Serialization.Serialization.SerializedActorPath(value));
    }

    public IActorRef Read(ref MessagePackReader reader)
    {
        reader.ReadArrayHeader();
        reader.ReadString();
        return _system.Provider.ResolveActorRef(reader.ReadString()!);
    }

    public int SizeOf(IActorRef value) =>
        MessagePackSizes.SizeOfArrayHeader(2) +
        MessagePackSizes.SizeOfString("ref") +
        MessagePackSizes.SizeOfString(Akka.Serialization.Serialization.SerializedActorPath(value));
}

[AkkaSerializer<IFormatterPositionProtocol>("formatter-position-test", 122001)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
[AkkaSerializerFormatter<ActorPath, ActorPathFormatter>]
[AkkaSerializerFormatter<PositionTag, PositionTagFormatter>]
[AkkaSerializerFormatter<PositionCelsius, PositionCelsiusFormatter>]
[AkkaSerializerFormatter<UnsizedTag, UnsizedTagFormatter>]
[AkkaSerializerFormatter<IActorRef, PositionRefFormatter>]
public sealed partial class FormatterPositionSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializer<INativeRefProtocol>("native-ref-position-test", 122002)]
public sealed partial class NativeRefPositionSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "native-ref-list-v1")]
public sealed record NativeRefListMessage(
    [property: AkkaField(1)] List<IActorRef> Refs) : INativeRefProtocol;

[AkkaSerializable(Manifest = "address-positions-v1")]
public sealed record AddressPositions(
    [property: AkkaField(1)] List<Address>? ListOf,
    [property: AkkaField(2)] Address[]? ArrayOf,
    [property: AkkaField(3)] ImmutableArray<Address> ImmutableArrayOf,
    [property: AkkaField(4)] IReadOnlyList<Address>? ReadOnlyListOf,
    [property: AkkaField(5)] IReadOnlyCollection<Address>? ReadOnlyCollectionOf,
    [property: AkkaField(6)] ImmutableList<Address>? ImmutableListOf,
    [property: AkkaField(7)] ImmutableHashSet<Address>? ImmutableSetOf,
    [property: AkkaField(8)] Dictionary<Address, long>? ByKey,
    [property: AkkaField(9)] IReadOnlyDictionary<Address, string>? ReadOnlyByKey,
    [property: AkkaField(10)] ImmutableDictionary<Address, int>? ImmutableByKey,
    [property: AkkaField(11)] Dictionary<string, Address>? ByValue,
    [property: AkkaField(12)] IReadOnlyDictionary<string, Address>? ReadOnlyByValue,
    [property: AkkaField(13)] ImmutableDictionary<string, Address>? ImmutableByValue,
    [property: AkkaField(14)] Dictionary<Address, Address>? KeyAndValue,
    [property: AkkaField(15)] List<Address?> NullableElements,
    [property: AkkaField(16)] List<List<Address>>? Nested,
    [property: AkkaField(17)] Dictionary<string, List<Address>>? MapOfLists) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "list-of-address-v1")]
public sealed record ListOfAddressMessage(
    [property: AkkaField(1)] List<Address> Addresses) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "single-address-v1")]
public sealed record SingleAddressMessage(
    [property: AkkaField(1)] Address Address) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "nullable-address-elements-v1")]
public sealed record NullableAddressElementsMessage(
    [property: AkkaField(1)] List<Address?> Addresses) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "address-to-address-v1")]
public sealed record AddressToAddressMessage(
    [property: AkkaField(1)] Dictionary<Address, Address> Map) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "tag-positions-v1")]
public sealed record TagPositions(
    [property: AkkaField(1)] List<PositionTag>? ListOf,
    [property: AkkaField(2)] PositionTag[]? ArrayOf,
    [property: AkkaField(3)] Dictionary<PositionTag, int>? ByKey,
    [property: AkkaField(4)] Dictionary<int, PositionTag>? ByValue,
    [property: AkkaField(5)] Dictionary<PositionTag, PositionTag>? KeyAndValue,
    [property: AkkaField(6)] ImmutableHashSet<PositionTag>? ImmutableSetOf) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "celsius-positions-v1")]
public sealed record CelsiusPositions(
    [property: AkkaField(1)] List<PositionCelsius>? ListOf,
    [property: AkkaField(2)] PositionCelsius[]? ArrayOf,
    [property: AkkaField(3)] List<PositionCelsius?>? NullableListOf,
    [property: AkkaField(4)] ImmutableArray<PositionCelsius?> ImmutableArrayOfNullable,
    [property: AkkaField(5)] Dictionary<PositionCelsius, string>? ByKey,
    [property: AkkaField(6)] Dictionary<string, PositionCelsius?>? ByNullableValue) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "ref-positions-v1")]
public sealed record RefPositions(
    [property: AkkaField(1)] List<IActorRef>? ListOf,
    [property: AkkaField(2)] IReadOnlyList<IActorRef>? ReadOnlyListOf,
    [property: AkkaField(3)] Dictionary<string, IActorRef>? ByValue,
    [property: AkkaField(4)] Dictionary<IActorRef, int>? ByKey,
    [property: AkkaField(5)] ImmutableArray<IActorRef> ImmutableArrayOf) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "actor-path-positions-v1")]
public sealed record ActorPathPositions(
    [property: AkkaField(1)] List<ActorPath> ListOf,
    [property: AkkaField(2)] Dictionary<ActorPath, ActorPath> KeyAndValue) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "mixed-v1")]
public sealed record MixedMessage(
    [property: AkkaField(1)] List<string> Natives,
    [property: AkkaField(2)] List<Address> Addresses) : IFormatterPositionProtocol;

/// <summary>A formatter that cannot cheaply size its value, so it reports <see cref="SerializerV2.UnknownSize"/>.</summary>
public sealed record UnsizedTag(string Value);

public sealed class UnsizedTagFormatter : IAkkaMessagePackFormatter<UnsizedTag>
{
    public void Write(ref MessagePackWriter writer, UnsizedTag value) => writer.Write(value.Value);

    public UnsizedTag Read(ref MessagePackReader reader) => new(reader.ReadString() ?? string.Empty);

    public int SizeOf(UnsizedTag value) => Akka.Serialization.SerializerV2.UnknownSize;
}

[AkkaSerializable(Manifest = "unsized-element-v1")]
public sealed record UnsizedElementMessage(
    [property: AkkaField(1)] List<UnsizedTag> Tags) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "unsized-key-v1")]
public sealed record UnsizedKeyMessage(
    [property: AkkaField(1)] Dictionary<UnsizedTag, int> Tags) : IFormatterPositionProtocol;

[AkkaSerializable(Manifest = "unsized-value-v1")]
public sealed record UnsizedValueMessage(
    [property: AkkaField(1)] Dictionary<int, UnsizedTag> Tags) : IFormatterPositionProtocol;
