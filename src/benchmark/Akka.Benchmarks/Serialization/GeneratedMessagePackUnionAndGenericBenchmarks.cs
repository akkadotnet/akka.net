//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackUnionAndGenericBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using Akka.Serialization.V2;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// Companion to <see cref="GeneratedMessagePackSerializerBenchmarks"/>: covers two source-generated
/// features that predate that file and had zero benchmark coverage until now --
/// <c>[AkkaUnion]</c> manifest-discriminated union fields (see
/// Akka.Serialization.V2.Tests.GeneratedUnionSpec) and closed-generic <c>[AkkaSerializable&lt;T&gt;]</c>
/// registrations (see Akka.Serialization.V2.Tests.GeneratedClosedGenericSpec), including the combined
/// "generic wrapper over a union interface" shape that motivated both features.
/// </summary>
/// <remarks>
/// Split into its own file (rather than folded into <see cref="GeneratedMessagePackSerializerBenchmarks"/>)
/// so it can evolve independently while the generator itself is still under active development;
/// same conventions throughout (ActorSystem/Serialization plumbing, <see cref="MicroBenchmarkConfig"/>,
/// <see cref="MemoryDiagnoser"/>, payload sizes in the same ballpark as <c>SubmitOrder</c>).
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class GeneratedMessagePackUnionAndGenericBenchmarks
{
    private ExtendedActorSystem _system = null!;

    private Serializer _unionSerializer = null!;
    private Serializer _genericSerializer = null!;

    private UnionBenchmarkEnvelope _unionRepresentative = null!;
    private UnionBenchmarkEnvelope _unionWorstCase = null!;
    private byte[] _unionRepresentativeBytes = null!;
    private byte[] _unionWorstCaseBytes = null!;
    private string _unionManifest = null!;

    private GenericBenchmarkWrapper<OrderDetailPayload> _closedGenericWrapper = null!;
    private byte[] _closedGenericWrapperBytes = null!;
    private string _closedGenericWrapperManifest = null!;

    private GenericBenchmarkWrapper<IOrderEventBenchmark> _combinedWrapper = null!;
    private byte[] _combinedWrapperBytes = null!;
    private string _combinedWrapperManifest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var setup = ActorSystemSetup.Create(Akka.Serialization.SerializationSetup.Create(system =>
        {
            var union = UnionBenchmarkSerializer.CreateRegistration().CreateDetails(system);
            var generic = GenericBenchmarkSerializer.CreateRegistration().CreateDetails(system);
            return ImmutableHashSet.Create(union, generic);
        }));
        _system = (ExtendedActorSystem)ActorSystem.Create("generated-messagepack-union-generic-bench", setup);

        // Representative member: OrderPlacedEvent is the FIRST type named in
        // [AkkaUnion(typeof(OrderPlacedEvent), typeof(OrderShippedEvent), typeof(OrderCancelledEvent), typeof(OrderRefundedEvent))]
        // on IOrderEventBenchmark below, so its write dispatch matches on the first `if (runtimeType == typeof(...))`
        // check the generator emits for GenerateUnionWrite.
        _unionRepresentative = new UnionBenchmarkEnvelope(
            "union-env-1",
            new OrderPlacedEvent("order-90001", Guid.Parse("11111111-2222-3333-4444-555555555555"), 4, 199.95m));

        // Worst case: OrderRefundedEvent is the LAST type named in the same [AkkaUnion] list. Union
        // write dispatch is a sequential `if (runtimeType == typeof(member)) { ...; return; }` chain in
        // declaration order (AkkaSerializerGenerator.GenerateUnionWrite) -- unlike union READ, which
        // dispatches on the manifest string through a `switch` (a jump table, not sequential), so there
        // is no equivalent read-side "worst case". Serializing OrderRefundedEvent must fall through three
        // failed type-equality checks before it matches.
        _unionWorstCase = new UnionBenchmarkEnvelope(
            "union-env-2",
            new OrderRefundedEvent("order-90002", 49.99m, "damaged in transit", DateTimeOffset.FromUnixTimeMilliseconds(1_735_776_000_000)));

        _unionSerializer = _system.Serialization.FindSerializerFor(_unionRepresentative);
        _unionManifest = Akka.Serialization.Serialization.ManifestFor(_unionSerializer, _unionRepresentative);
        _unionRepresentativeBytes = _system.Serialization.Serialize(_unionRepresentative);
        _unionWorstCaseBytes = _system.Serialization.Serialize(_unionWorstCase);

        _closedGenericWrapper = new GenericBenchmarkWrapper<OrderDetailPayload>(
            "wrapper-1",
            new OrderDetailPayload(
                "order-90003",
                Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa"),
                12500043L,
                true,
                1337.42m,
                DateTimeOffset.FromUnixTimeMilliseconds(1_735_689_600_000),
                OrderPriority.High),
            42L);
        _genericSerializer = _system.Serialization.FindSerializerFor(_closedGenericWrapper);
        _closedGenericWrapperManifest = Akka.Serialization.Serialization.ManifestFor(_genericSerializer, _closedGenericWrapper);
        _closedGenericWrapperBytes = _system.Serialization.Serialize(_closedGenericWrapper);

        // The flagship combined scenario: a registered closed-generic construction
        // (GenericBenchmarkWrapper<IOrderEventBenchmark>) whose type argument is itself a
        // manifest-discriminated union interface. No object boundary anywhere on the wire: the wrapper's
        // Payload field is statically typed as IOrderEventBenchmark, picks up that interface's
        // type-level [AkkaUnion] declaration automatically, and is dispatched/encoded inline.
        _combinedWrapper = new GenericBenchmarkWrapper<IOrderEventBenchmark>(
            "wrapper-2",
            new OrderPlacedEvent("order-90004", Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"), 6, 349.5m),
            43L);
        _combinedWrapperManifest = Akka.Serialization.Serialization.ManifestFor(_genericSerializer, _combinedWrapper);
        _combinedWrapperBytes = _system.Serialization.Serialize(_combinedWrapper);

        if (_unionSerializer is not UnionBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated union serializer, got [{_unionSerializer.GetType()}].");
        if (_genericSerializer is not GenericBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated closed-generic serializer, got [{_genericSerializer.GetType()}].");

        Console.WriteLine($"Union representative member payload size: {_unionRepresentativeBytes.Length} bytes");
        Console.WriteLine($"Union worst-case (last-declared) member payload size: {_unionWorstCaseBytes.Length} bytes");
        Console.WriteLine($"Closed-generic wrapper payload size: {_closedGenericWrapperBytes.Length} bytes");
        Console.WriteLine($"Closed-generic + union combined payload size: {_combinedWrapperBytes.Length} bytes");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _system.Terminate();
    }

    [Benchmark]
    public byte[] Union_representative_member_serialize()
    {
        return _system.Serialization.Serialize(_unionRepresentative);
    }

    [Benchmark]
    public object Union_representative_member_deserialize()
    {
        return _system.Serialization.Deserialize(_unionRepresentativeBytes, _unionSerializer.Identifier, _unionManifest);
    }

    [Benchmark]
    public byte[] Union_worst_case_last_declared_member_serialize()
    {
        return _system.Serialization.Serialize(_unionWorstCase);
    }

    [Benchmark]
    public object Union_worst_case_last_declared_member_deserialize()
    {
        return _system.Serialization.Deserialize(_unionWorstCaseBytes, _unionSerializer.Identifier, _unionManifest);
    }

    [Benchmark]
    public byte[] ClosedGeneric_wrapper_serialize()
    {
        return _system.Serialization.Serialize(_closedGenericWrapper);
    }

    [Benchmark]
    public object ClosedGeneric_wrapper_deserialize()
    {
        return _system.Serialization.Deserialize(_closedGenericWrapperBytes, _genericSerializer.Identifier, _closedGenericWrapperManifest);
    }

    [Benchmark]
    public byte[] ClosedGeneric_union_payload_combined_serialize()
    {
        return _system.Serialization.Serialize(_combinedWrapper);
    }

    [Benchmark]
    public object ClosedGeneric_union_payload_combined_deserialize()
    {
        return _system.Serialization.Deserialize(_combinedWrapperBytes, _genericSerializer.Identifier, _combinedWrapperManifest);
    }
}

public interface IUnionBenchmarkProtocol
{
}

[AkkaSerializer<IUnionBenchmarkProtocol>("union-benchmark", 120004)]
public sealed partial class UnionBenchmarkSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

public interface IGenericBenchmarkProtocol
{
}

[AkkaSerializer<IGenericBenchmarkProtocol>("closed-generic-benchmark", 120005)]
[AkkaSerializable<GenericBenchmarkWrapper<OrderDetailPayload>>(Manifest = "generic-wrapper-order-detail-v1")]
[AkkaSerializable<GenericBenchmarkWrapper<IOrderEventBenchmark>>(Manifest = "generic-wrapper-order-event-v1")]
public sealed partial class GenericBenchmarkSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

/// <summary>
/// The union's static field type, carrying the type-level member declaration -- inherited by every
/// field typed as <see cref="IOrderEventBenchmark"/>, including the substituted <c>T</c> of
/// <see cref="GenericBenchmarkWrapper{T}"/> in the combined scenario. Declaration order here IS write
/// dispatch order (see <see cref="GeneratedMessagePackUnionAndGenericBenchmarks.Union_worst_case_last_declared_member_serialize"/>).
/// </summary>
[AkkaUnion(typeof(OrderPlacedEvent), typeof(OrderShippedEvent), typeof(OrderCancelledEvent), typeof(OrderRefundedEvent))]
public interface IOrderEventBenchmark
{
}

[AkkaSerializable(Manifest = "union-benchmark-envelope-v1")]
public sealed record UnionBenchmarkEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1)] IOrderEventBenchmark Event) : IUnionBenchmarkProtocol;

[AkkaSerializable(Manifest = "order-placed-benchmark-v1")]
public sealed record OrderPlacedEvent(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] int Quantity,
    [property: AkkaField(3)] decimal Total) : IOrderEventBenchmark;

[AkkaSerializable(Manifest = "order-shipped-benchmark-v1")]
public sealed record OrderShippedEvent(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] string Carrier,
    [property: AkkaField(2)] string TrackingNumber,
    [property: AkkaField(3)] DateTimeOffset ShippedAt) : IOrderEventBenchmark;

[AkkaSerializable(Manifest = "order-cancelled-benchmark-v1")]
public sealed record OrderCancelledEvent(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] string Reason) : IOrderEventBenchmark;

[AkkaSerializable(Manifest = "order-refunded-benchmark-v1")]
public sealed record OrderRefundedEvent(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] decimal RefundAmount,
    [property: AkkaField(2)] string Reason,
    [property: AkkaField(3)] DateTimeOffset RefundedAt) : IOrderEventBenchmark;

/// <summary>
/// A generic protocol message registered as two distinct closed constructions on
/// <see cref="GenericBenchmarkSerializer"/> -- one over a plain <see cref="OrderDetailPayload"/>
/// (the ordinary closed-generic case) and one over <see cref="IOrderEventBenchmark"/> (the combined
/// generic-over-union case). The open definition itself is never serialized directly, matching
/// Akka.Serialization.V2.Tests.GeneratedClosedGenericSpec's <c>Wrapper&lt;T&gt;</c>/<c>EventWrapper&lt;T&gt;</c>.
/// </summary>
[AkkaSerializable]
public sealed record GenericBenchmarkWrapper<T>(
    [property: AkkaField(0)] string WrapperId,
    [property: AkkaField(1)] T Payload,
    [property: AkkaField(2)] long SequenceNr) : IGenericBenchmarkProtocol;

/// <summary>
/// Plain closed-generic payload, field count and types deliberately mirroring
/// <see cref="SubmitOrder"/> in <see cref="GeneratedMessagePackSerializerBenchmarks"/> (minus the
/// <see cref="IActorRef"/> field, already exercised there) so per-field costs stay comparable across
/// both benchmark files.
/// </summary>
[AkkaSerializable]
public sealed record OrderDetailPayload(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] long SequenceNr,
    [property: AkkaField(3)] bool Expedited,
    [property: AkkaField(4)] decimal Total,
    [property: AkkaField(5)] DateTimeOffset CreatedAt,
    [property: AkkaField(6)] OrderPriority Priority);
