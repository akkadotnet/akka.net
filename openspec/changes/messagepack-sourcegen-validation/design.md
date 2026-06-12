## Context

The POC at `Aaronontheweb/AkkaSerializationPoC` validated the preferred direction for generated serialization:

- MessagePack is the default codec.
- Generated serializers use MessagePack-CSharp directly on their hot path.
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

`Akka.Serialization.V2` owns MessagePack dependencies, attributes, Akka-specific MessagePack helper conventions, and source generator integration.

Core Akka owns only `SerializerV2` and compatibility infrastructure.

### 2. Direct MessagePack Reader / Writer

Generated serializers should create one `MessagePackWriter` per `Serialize` call and one `MessagePackReader` per `Deserialize` call, then pass those cursors by `ref` through generated helper methods. This avoids recreating `MessagePackReader` / `MessagePackWriter` per field while preserving MessagePack-CSharp's cursor semantics.

Do not keep separate `AkkaReader` or `AkkaWriter` public wrapper classes for generated serializers. Tests that need to inspect or craft payloads should use MessagePack-CSharp cursors directly.

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

The initial generator should force immutable message designs. Supported shapes should start with records / primary constructors, constructor-bound immutable classes, and init-only field or property assignment. Nested structures are required early, should support arbitrary-depth acyclic schemas, and must use explicit `[AkkaField]` IDs of their own. Nested value objects do not need serializer manifests unless they are also top-level protocol messages dispatched directly by Akka serialization. Nested value-object types still need an explicit generated serialization definition via `[AkkaSerializable]`; otherwise the generator should fail compilation.

Factory methods, mutable setter-centric models, inheritance-heavy object graphs, and arbitrary polymorphic discovery are out of scope for the first production slice.

### 7.2 Collection Scope

Initial collection support should cover immutable and read-only collection shapes: `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyDictionary<TKey,TValue>`, and arrays where needed for interop or performance. Interface collection targets must document their concrete deserialization type.

### 8. Wrapper Validation Without Wire Replacement

Generated payloads should be validated inside existing Akka.Delivery and DistributedData wrappers where practical. This proves nested serializer behavior without changing those subsystems' default protobuf wire formats.

Envelope payloads are serializer boundaries, not nested generated schemas. A generated MessagePack envelope should preserve the wrapped payload's Akka serializer id, manifest, and serialized bytes, then recover it through normal Akka deserialization. This matches existing Akka.Remote `WrappedPayloadSupport`, Akka.Delivery payload handling, and DistributedData `OtherMessage` conventions.

Generated object payload boundaries are expressed with `[AkkaEnvelopePayload]` on the wrapper field. The marker is field-level because the same message type may be serialized inline in one schema and treated as an Akka serializer boundary in an envelope schema. The generator emits runtime serializer lookup for marked fields and does not structurally MessagePack-encode the marked payload value.

Pre-serialized envelope payloads, such as Akka.Delivery `ChunkedMessage`, are a related but distinct shape: they already carry serialized bytes plus serializer id and manifest. Generated envelope support should distinguish object payload fields that require serializer lookup from already-captured serialized payload metadata.

`SerializerV2.SizeHint` is an exact-size contract: non-negative values mean the exact number of bytes `Serialize` will write, while `SerializerV2.UnknownSize` means exact size is not cheaply known. Unknown size is transitive through nested generated values and envelope payloads. If any nested field or payload serializer returns `UnknownSize`, every enclosing generated serializer must return `UnknownSize`. Generated serializers should report exact sizes only when they can prove the complete encoded size.

MessagePack `bin` fields require the byte length before payload bytes are written. Envelope payloads with unknown or expensive-to-compute length need a staging buffer before the outer `bin` field can be written. Benchmarking showed that forcing exact-size precomputation before envelope writes can duplicate expensive work, such as UTF8 string sizing and actor-ref path serialization, and can be slower than staging. The default envelope path should therefore stage V2 payload bytes with a reusable buffer and dispatch V2 payload reads from `ReadOnlySequence<byte>` to avoid the read-side byte-array copy. Exact-size direct writing should be a future targeted optimization only for schemas where size calculation is demonstrably cheap.

The original SerializerV2 proof-of-concept demonstrated a different optimization: inline structural nesting, where a V2 envelope writes serializer id and manifest metadata, then delegates to the inner V2 serializer using the same MessagePack writer so the payload becomes a nested MessagePack value instead of an opaque `bin`. That design is feasible across assemblies if generated MessagePack serializers expose an explicit cross-assembly MessagePack contract and are registered normally, but it is not equivalent to Akka's default serializer boundary. It only works for V2 MessagePack serializers, requires a distinct wire shape or version marker from opaque payload bytes, and custom or V1 serializers still need a binary-blob fallback. Closed generated unions are also feasible across referenced assemblies, but only from explicit user-declared payload sets; they should not rely on runtime assembly scanning or automatic cross-assembly discovery.

### 8.1 Future Built-in Serializer Migration Strategy

Future MessagePack integrations for built-in Akka.Remote, Akka.Persistence, Akka.Delivery, or DistributedData serializers should fork existing serializers instead of changing their wire format in place. Existing serializer IDs must remain available for reads, and new MessagePack/generated serializers must use new unique serializer IDs and manifests. This preserves mixed-version compatibility, persisted journal and snapshot readability, and user applications that depend on custom or legacy serializer bindings.

Read compatibility and write selection should be treated separately. New integrations should read both old and new serializer IDs where practical, while writes should remain controlled by configuration, feature flags, or explicit protocol capability checks. Remoting and cluster features must not emit new serializer IDs to peers that are not known to support them; early releases may require an "all nodes upgraded before enabling" rule rather than automatic negotiation.

Persistence needs the strictest compatibility rule: historical events and snapshots are durable wire contracts. New serializers can be offered for opt-in new writes, but old serializers must remain readable indefinitely unless a separate migration tool and operational process is provided. DistributedData and Akka.Delivery should follow the same serializer-boundary model for arbitrary user payloads, because replicated state and delivery envelopes can carry payloads owned by application serializers outside Akka's control.

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
