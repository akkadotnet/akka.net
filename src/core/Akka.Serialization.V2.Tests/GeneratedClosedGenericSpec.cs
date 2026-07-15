//-----------------------------------------------------------------------
// <copyright file="GeneratedClosedGenericSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Specs for the generic <c>[AkkaSerializable&lt;T&gt;]</c> form: registering CLOSED generic constructions of a
/// generic <c>[AkkaSerializable]</c> type on the serializer module, the same closed-construction
/// registration model System.Text.Json's source generator uses for <c>[JsonSerializable]</c>.
/// Each registered construction behaves as its own top-level message with its own manifest, with
/// generic fields resolved against the concrete type arguments.
/// </summary>
public sealed class GeneratedClosedGenericSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private ClosedGenericTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("generated-closed-generic-spec");
        _serializer = new ClosedGenericTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "Registered closed construction should round-trip with its generic field typed inline")]
    public void Closed_construction_should_round_trip_typed_inline()
    {
        var message = new Wrapper<OrderRequest>("wrap-1", new OrderRequest("order-1", 5), 3);

        var result = RoundTrip(message);

        result.Should().Be(message);
        result.Payload.Should().BeOfType<OrderRequest>();
    }

    [Fact(DisplayName = "Distinct closed constructions of the same generic should dispatch by distinct manifests")]
    public void Distinct_constructions_should_dispatch_by_distinct_manifests()
    {
        var requestWrapper = new Wrapper<OrderRequest>("wrap-2", new OrderRequest("order-2", 7), null);
        var receiptWrapper = new Wrapper<OrderReceipt>("wrap-3", new OrderReceipt("receipt-1"), 1);

        _serializer.Manifest(requestWrapper).Should().Be("wrap-request-v1");
        _serializer.Manifest(receiptWrapper).Should().Be("wrap-receipt-v1");

        RoundTrip(requestWrapper).Should().Be(requestWrapper);
        RoundTrip(receiptWrapper).Should().Be(receiptWrapper);
    }

    [Fact(DisplayName = "Closed construction wire format should inline the substituted payload with no discriminator")]
    public void Closed_construction_wire_format_should_inline_payload()
    {
        // Contrast with both [AkkaEnvelopePayload] ({id, manifest, bytes}) and [AkkaUnion]
        // ({manifest, fields}): a T-typed field in a registered construction is statically known,
        // so it encodes as a plain nested field map with NO discriminator of any kind.
        var message = new Wrapper<OrderRequest>("wrap-4", new OrderRequest("order-4", 9), null);
        var bytes = _serializer.ToBinary(message);

        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(3);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("wrap-4");
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("order-4");
        reader.ReadInt32().Should().Be(2);
        reader.ReadInt32().Should().Be(9);
        reader.ReadInt32().Should().Be(3);
        reader.TryReadNil().Should().BeTrue();
        reader.Consumed.Should().Be(bytes.Length);
    }

    [Fact(DisplayName = "Closed construction SizeHint should be exact")]
    public void Closed_construction_size_hint_should_be_exact()
    {
        var message = new Wrapper<OrderReceipt>("wrap-5", new OrderReceipt("receipt-5"), 42);

        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Closed construction should be usable as a nested field of an ordinary message")]
    public void Closed_construction_should_nest_inside_ordinary_message()
    {
        var message = new WrapperCarrier("carrier-1", new Wrapper<OrderReceipt>("wrap-6", new OrderReceipt("receipt-6"), null));

        RoundTrip(message).Should().Be(message);
    }

    [Fact(DisplayName = "Generic wrapper with a union payload should round-trip the combined scenario fully typed")]
    public void Generic_wrapper_with_union_payload_should_round_trip()
    {
        // THE motivating scenario from the design issue: a generic wrapper registered as a closed
        // construction whose payload field is a closed manifest-discriminated union. Fully typed,
        // inline, exact-sized -- no object boundary anywhere.
        var placed = new EventWrapper<IOrderEvent>("evt-1", new OrderPlaced("order-10", 2));
        var cancelled = new EventWrapper<IOrderEvent>("evt-2", new OrderCancelled("order-11", "late"));

        var placedResult = RoundTrip(placed);
        placedResult.Should().Be(placed);
        placedResult.Body.Should().BeOfType<OrderPlaced>();

        var cancelledResult = RoundTrip(cancelled);
        cancelledResult.Should().Be(cancelled);
        cancelledResult.Body.Should().BeOfType<OrderCancelled>();

        _serializer.SizeHint(placed).Should().Be(_serializer.ToBinary(placed).Length);
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IClosedGenericTestProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface IClosedGenericTestProtocol
{
}

[AkkaSerializer<IClosedGenericTestProtocol>(Name = "closed-generic-test", SerializerId = 120404)]
[AkkaSerializable<Wrapper<OrderRequest>>(Manifest = "wrap-request-v1")]
[AkkaSerializable<Wrapper<OrderReceipt>>(Manifest = "wrap-receipt-v1")]
[AkkaSerializable<EventWrapper<IOrderEvent>>(Manifest = "event-wrap-v1")]
public sealed partial class ClosedGenericTestSerializer : MessagePackSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

/// <summary>
/// A generic protocol message. The open definition carries the [AkkaField] indices but is never
/// serialized itself -- only the closed constructions registered on the serializer are.
/// </summary>
[AkkaSerializable]
public sealed record Wrapper<T>(
    [property: AkkaField(1)] string WrapperId,
    [property: AkkaField(2)] T Payload,
    [property: AkkaField(3)] int? Priority) : IClosedGenericTestProtocol;

/// <summary>
/// The combined scenario: a generic wrapper whose payload property is a closed union. The
/// definition declares NO union of its own -- at instantiation time (T := IOrderEvent) the
/// substituted static type's TYPE-LEVEL [AkkaUnion] declaration on IOrderEvent is picked up, and
/// its members are validated against the substituted type.
/// </summary>
[AkkaSerializable]
public sealed record EventWrapper<T>(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2)] T Body)
    : IClosedGenericTestProtocol;

[AkkaSerializable(Manifest = "carrier-v1")]
public sealed record WrapperCarrier(
    [property: AkkaField(1)] string CarrierId,
    [property: AkkaField(2)] Wrapper<OrderReceipt> Inner) : IClosedGenericTestProtocol;

[AkkaSerializable]
public sealed record OrderRequest(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] int Quantity);

[AkkaSerializable]
public sealed record OrderReceipt(
    [property: AkkaField(1)] string ReceiptId);
