## Context

The POC at `Aaronontheweb/AkkaSerializationPoC` validated the preferred direction for generated serialization:

- MessagePack is the default codec.
- `AkkaWriter` and `AkkaReader` are concrete sealed wrappers over MessagePack.
- There is no generalized codec abstraction layer.
- Source generation provides compile-time validation and avoids reflection.
- Users explicitly register generated serializers through generated per-serializer helpers.

The source generator should not be developed against hypothetical serialization APIs. It should run after `serializer-v2` makes V2 canonical and after classic remoting and persistence are bridged. That lets generated serializers validate the exact API Artery will consume.

## Goals / Non-Goals

**Goals:**

- Implement user-facing source-generated MessagePack serialization on top of `SerializerV2`.
- Validate generated serializers through `Serialization`, classic remoting, events, and snapshots.
- Confirm V2 API details before Artery envelopes are built.
- Support AOT-oriented, reflection-free serializer code.
- Support common Akka protocol-family message shapes, including `IActorRef` reply-to fields.
- Produce an early benchmarkable POC before completing the full sourcegen matrix.
- Preserve V1/V2 coexistence.

**Non-Goals:**

- Replacing all built-in protobuf serializers.
- Adding MessagePack dependency to core Akka.
- Implementing Artery envelopes.
- Replacing classic remoting, persistence, Akka.Delivery, or DistributedData protobuf wrapper wire formats by default.
- Removing V1 serializer support.

## Decisions

### 1. MessagePack Package Outside Core Akka

`Akka.Serialization.V2` owns MessagePack dependencies, attributes, writer/reader helpers, and source generator integration.

Core Akka owns only `SerializerV2` and compatibility infrastructure.

### 2. Sealed Writer / Reader

Use sealed `AkkaWriter` and `AkkaReader` classes rather than codec interfaces.

Rationale: the POC showed this improves JIT devirtualization and keeps the API simpler.

### 3. Sourcegen Validates V2 API Before Artery

Generated serializers must prove:

- bytes-written/result reporting works,
- unknown-size fallback works,
- manifests work,
- V1 adapter coexistence works,
- persistence can store and recover generated payloads,
- classic remoting can send generated payloads.

### 4. Version-Tolerant Schema

Fields are explicitly indexed using `[AkkaField(index)]`. Generated code should support unknown trailing fields where possible.

### 5. Explicit Per-Serializer Registration

Generated serializers should expose registration helpers on the user-declared partial serializer class. Runtime assembly scanning is not part of the generated serializer path because it conflicts with NativeAOT and trimming goals.

The primary shape is:

```csharp
[AkkaSerializer(Name = "orders", SerializerId = 120001)]
public sealed partial class OrderSerializer : MessagePackSerializer<IOrderProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
    public static partial SerializationSetup CreateSetup();
}
```

`CreateSetup()` is a single-serializer convenience. Multi-serializer applications compose registrations explicitly into one `SerializationSetup`; the generator does not emit a cross-assembly aggregate.

### 6. Protocol Marker Grouping

Users declare a serializer module and a protocol marker interface. `[AkkaSerializable]` message types implement that interface. This is similar in spirit to `System.Text.Json` source-generated contexts, but it fits Akka protocol families better and avoids a second manually-maintained type list.

### 7. `IActorRef` Field Support

Generated serializers should support `IActorRef` fields by writing `Serialization.SerializedActorPath(actorRef)` and resolving through the serializer's `ExtendedActorSystem` on read. Empty paths represent `ActorRefs.NoSender` / null.

### 8. Wrapper Validation Without Wire Replacement

Generated payloads should be validated inside existing Akka.Delivery and DistributedData wrappers where practical. This proves nested serializer behavior without changing those subsystems' default protobuf wire formats.

### 9. Early Benchmark POC Stop Point

Before completing the full spec, produce a basic BenchmarkDotNet POC using real C# protocol-family messages. The first benchmark should compare generated MessagePack serialization against current baseline serializer behavior and report throughput/allocation/payload-size signals.

## Risks / Trade-offs

**Generator complexity**: keep diagnostics focused and add incrementally.

**MessagePack conventions**: document DateTime, Guid, decimal, nullable, collection, and nested object conventions.

**Benchmark interpretation**: the first benchmark is directional POC evidence, not final Artery performance proof.

**API churn**: if sourcegen finds V2 API problems, fix V2 before Artery starts.

**Persistence compatibility**: generated serializers must not compromise stored payload readability.
