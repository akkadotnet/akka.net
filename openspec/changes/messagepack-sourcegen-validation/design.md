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

Fields are explicitly indexed using `[AkkaField(index)]`, and those indexes are encoded as field IDs in the MessagePack payload. The MessagePack representation should not depend on constructor or property array position for compatibility.

Generated readers should skip unknown field IDs. Schema evolution should stay close to traditional MessagePack schema behavior: once a field ID is published, it must not be reused for a different meaning; renames are safe when the field ID stays stable; removing a field reserves its ID forever. Changing a field type is not compatible and should fail through normal MessagePack reader/type validation while older message versions are still in circulation.

The source generator should not add extra historical schema validation, swapped-field detection, or schema-registry style checks. Analyzer rules should focus on the current compilation shape and obvious protocol-family mistakes.

### 5. Explicit Per-Serializer Registration

Generated serializers should expose registration helpers on the user-declared partial serializer class. Runtime assembly scanning is not part of the generated serializer path because it conflicts with NativeAOT and trimming goals.

The primary shape is:

```csharp
[AkkaSerializer(Name = "orders", SerializerId = 120001)]
public sealed partial class OrderSerializer : MessagePackSerializer<IOrderProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}
```

Generated serializers return reusable registration data. Non-hosted applications compose registrations explicitly into one `SerializationSetup`; Akka.Hosting integrations should feed generated registrations into Akka.Hosting's serializer accumulator. The generator does not emit a cross-assembly aggregate or a generated `CreateSetup()` helper.

### 6. Protocol Marker Grouping

Users declare a serializer module and a protocol marker interface. `[AkkaSerializable]` message types implement that interface. This is similar in spirit to `System.Text.Json` source-generated contexts, but it fits Akka protocol families better and avoids a second manually-maintained type list.

### 7. `IActorRef` Field Support

Generated serializers should support `IActorRef` fields by writing `Serialization.SerializedActorPath(actorRef)` and resolving through the serializer's `ExtendedActorSystem` on read. Empty paths represent `ActorRefs.NoSender` / null.

`ActorRefs.NoSender` is treated as the null-equivalent actor reference value for generated payloads.

### 7.1 Message Shape Scope

The initial generator should force immutable message designs. Supported shapes should start with records / primary constructors, constructor-bound immutable classes, and init-only field or property assignment. Nested structures are required early and must use explicit `[AkkaField]` IDs of their own. Nested value objects do not need serializer manifests unless they are also top-level protocol messages dispatched directly by Akka serialization. Nested value-object types still need an explicit generated serialization definition via `[AkkaSerializable]`; otherwise the generator should fail compilation.

Factory methods, mutable setter-centric models, inheritance-heavy object graphs, and arbitrary polymorphic discovery are out of scope for the first production slice.

### 7.2 Collection Scope

Initial collection support should cover immutable and read-only collection shapes: `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyDictionary<TKey,TValue>`, and arrays where needed for interop or performance. Interface collection targets must document their concrete deserialization type.

### 8. Wrapper Validation Without Wire Replacement

Generated payloads should be validated inside existing Akka.Delivery and DistributedData wrappers where practical. This proves nested serializer behavior without changing those subsystems' default protobuf wire formats.

### 9. Early Benchmark POC Stop Point

Before completing the full spec, produce a basic BenchmarkDotNet POC using real C# protocol-family messages. The first benchmark should compare generated MessagePack serialization against current baseline serializer behavior and report throughput/allocation/payload-size signals.

### 10. Packaging

Ship as one user-facing package if packing can be done cleanly. Internal split projects for runtime and generator are acceptable, but users should not have to install a separate runtime package and generator package manually.

## Risks / Trade-offs

**Generator complexity**: keep diagnostics focused and add incrementally.

**MessagePack conventions**: document DateTime, Guid, decimal, nullable, collection, and nested object conventions.

**Benchmark interpretation**: the first benchmark is directional POC evidence, not final Artery performance proof.

**API churn**: if sourcegen finds V2 API problems, fix V2 before Artery starts.

**Persistence compatibility**: generated serializers must not compromise stored payload readability.
