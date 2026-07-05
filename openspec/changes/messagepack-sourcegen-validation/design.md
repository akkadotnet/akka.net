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

### 11. Foreign-Type Formatters

The generator's nested-value-object rule (Decision 7.1: every nested field type must carry `[AkkaSerializable]` and its own explicit `[AkkaField]` schema) fails closed for types the generator cannot annotate. Core Akka types such as `Akka.Actor.Address` are the canonical case: `[AkkaSerializable]` lives in `Akka.Serialization.V2`, which references `Akka` — annotating `Address` directly would require a dependency cycle. Before this decision, a serializer with an `Address`-typed field (nested or top-level) always failed compilation with `AKKASG007` (`MissingNestedSerializableDefinition`), with no escape hatch. This is exactly the friction that forced Artery's handshake messages (`HandshakeReq`/`HandshakeRsp`, which carry `UniqueAddress`/`Address`) onto a hand-rolled `MessagePackSerializer<T>` subclass (`Akka.Remote.Artery.ArteryControlMessageSerializer`) instead of a generated one.

Two options were evaluated:

1. **Relocate or metadata-match `[AkkaSerializable]` so core types can opt in.** Either move the attribute to a dependency-free assembly core Akka can reference, or have the generator match attributes by metadata name/shape across assemblies instead of a single compile-time attribute reference. **Rejected.** The generator is syntax-driven (`ForAttributeWithMetadataName` on the *current* compilation's syntax trees): `[AkkaSerializable]` types declared in a *referenced* assembly (like core `Akka`) are invisible to it regardless of where the attribute type lives, because there is no syntax node to walk in the referencing compilation. Making that work would require a metadata-based schema-extraction redesign (reading previously-generated schema facts back out of referenced assemblies), which is a much larger change than the problem justifies. It also permanently couples core Akka types to a durable generated-wire schema the moment they're annotated, and it still cannot express context-dependent encodings — `IActorRef` and `ActorPath` fields need transport-aware address substitution (`Serialization.SerializedActorPath`, `ActorPath.ToSerializationFormatWithAddress`) that no static per-type schema can capture.

2. **A per-serializer formatter escape hatch.** **Chosen.** A serializer opts a specific foreign type into hand-written encoding via `[AkkaSerializerFormatter(typeof(TTarget), typeof(TFormatter))]`, where `TFormatter` implements `Akka.Serialization.V2.IAkkaMessagePackFormatter<TTarget>`:

   ```csharp
   public interface IAkkaMessagePackFormatter<T>
   {
       void Write(ref MessagePackWriter writer, T value);
       T Read(ref MessagePackReader reader);
       int SizeOf(T value); // exact byte count, or SerializerV2.UnknownSize
   }
   ```

   The contract mirrors the rest of the generator's field conventions: `Write`/`Read` must be symmetric; `value` is never null/absent for non-nullable fields — the generator, not the formatter, owns MessagePack nil encoding for nullable fields; `SizeOf` must return the *exact* encoded byte count or `UnknownSize`, and an incorrect non-negative value silently corrupts the enclosing serializer's `SizeHint` contract the same way a buggy nested `SizeOf<Message>` would. `Write` must also produce exactly ONE top-level MessagePack value (wrap multiple values in a single array or map): the generated map framing and the unknown-field forward-compatibility path (`reader.Skip()`) both depend on one field id mapping to one MessagePack value, and multiple top-level values desync older readers during rolling upgrades. The exact-size encoding math the generated serializers use is exposed as the public `MessagePackSizes` static class precisely so external hand-written formatters — Akka.Remote's future formatters and user formatters in other assemblies alike — can compose the same helpers when honoring the exact-or-`UnknownSize` contract instead of hand-deriving MessagePack header sizes.

   `[AkkaSerializerFormatter]` is applied to the `[AkkaSerializer]` partial class and is **serializer-scoped**: the same foreign type can be formatted differently (or not at all) by different serializers in the same compilation, because the generator resolves field kinds per serializer in the output stage rather than globally at extract time. A formatter registration **overrides every field-kind resolution** the generator would otherwise infer for the target type — scalars, `Object`, `ActorRef`, `Enum`, `MissingSerializableDefinition`, and `Unsupported` alike — with one exception: `[AkkaEnvelopePayload]` always wins, because it is an explicit field-level marker for a distinct concern (an Akka serializer boundary, not a structural encoding). `Nullable<T>` fields match the formatter registration on the *underlying* value type, so `Address?` and `TestUniqueAddress?`-shaped fields both route through the same formatter as their non-nullable counterparts, with the generator handling the nil branch.

   Formatters are constructed once per generated serializer instance, in the generated constructor, using either a public parameterless constructor or a public constructor taking `Akka.Actor.ExtendedActorSystem` (for formatters that need system context, e.g. to resolve local vs. remote encoding). When BOTH constructors are present, the generator prefers the `ExtendedActorSystem` one: the generated serializer always has the system in hand, and system context is why a formatter declares that constructor in the first place — silently picking parameterless would drop it. Registering a formatter that is abstract, generic, doesn't implement `IAkkaMessagePackFormatter<TTarget>` for exactly the registered target type, or exposes neither usable constructor shape, fails compilation (`AKKASG008` invalid formatter type, `AKKASG010` no usable constructor) instead of silently falling back to the old nested-object behavior. Registering two formatters for the same target type on one serializer also fails compilation (`AKKASG009`), as does registering a target type that is not a plain named type — arrays and open or closed generics (`AKKASG011`) — rather than the registration silently doing nothing or colliding on the arity-less type name the generator uses for field matching.

   Two built-in formatters ship in `Akka.Serialization.V2`: `AddressFormatter` for `Akka.Actor.Address`, and `ActorPathFormatter` for `Akka.Actor.ActorPath`. `AddressFormatter`'s wire format is deliberately **byte-identical** to `ArteryControlMessageSerializer`'s hand-rolled `WriteAddress`/`ReadAddress`/`SizeOfAddress` (a 4-element array of `[Protocol, System, Host-or-nil, Port-or-nil]`), so a generated serializer that registers `[AkkaSerializerFormatter(typeof(Address), typeof(AddressFormatter))]` can read and write the exact bytes Artery's control-message serializer already produces on the wire today. `ActorPathFormatter` writes a single transport-aware string using the same convention the generator already uses for `IActorRef` fields: it reads the thread-static transport context (`Serialization.CurrentTransportInformation`, accessed directly via an `InternalsVisibleTo` grant to `Akka.Serialization.V2` so no exception is thrown or caught on the non-transport path) and renders the path with the transport's address via `ActorPath.ToSerializationFormatWithAddress` when one is set; outside any transport scope it falls back to the owning system's `Provider.DefaultAddress` when constructed with an `ExtendedActorSystem` (which generated serializers do automatically, since the generator prefers that constructor — matching `Serialization.SerializedActorPath` semantics, so the path stays remotely resolvable), and only to `ActorPath.ToSerializationFormat()` when it has no system at all. Because `SizeOf` and `Write` each read the thread-static context independently, transport-sensitive formatters require both calls to run under the same transport scope/thread for the exact-size contract to hold — the generated serializers and the Artery encode path do this naturally. This closes the loop that started `ArteryControlMessageSerializer`'s hand-rolled fallback: a generated serializer can now reproduce that exact wire format, so the hand-rolled class is a candidate for replacement by a generated one in a follow-up change, without a wire-format break.

   As a related but independently useful fix in the same change, the generator now emits the serializer partial class with the **declared accessibility of the user's serializer symbol** (`public` or `internal`) instead of hardcoding `public`. This has no direct dependency on the formatter escape hatch, but it was required to exercise `[AkkaSerializerFormatter]` against `internal` serializers (the shape Akka.Remote's own control-message serializer would need if it were later migrated to the generator), and there was no reason to gate it behind a separate change.

### 12. Oversized-Payload Determinism

Oversized-payload failure is deterministic and happens at encode time. The contract: a caller-imposed cap — a transport's maximum-frame-size expressed as `PooledPayloadWriter.maxCapacity` — makes any payload whose encoding would exceed that cap fail *during* `Serialize` with a typed `Akka.Serialization.PayloadSizeExceededException` carrying the attempted size and the configured cap. A truncated or corrupt frame is never observed downstream: the writer refuses the write that would cross the boundary, so no partial frame larger than the cap ever exists to hand to a transport.

The writer-side mechanism is serializer-v2 design.md Decision 12 (`PooledPayloadWriter` + ownership contract): every `GetSpan`/`GetMemory`/`Advance` that would push the written count past `maxCapacity` throws, and writer mechanics are covered by `Akka.Tests.Serialization.PooledPayloadWriterSpec`. What this change pins (`OversizedPayloadDeterminismSpec`, task 6.8) is the *serializer-side* half of the contract:

- The exception propagates out of the generated serializer's `Serialize` call, for both plain generated messages and `[AkkaEnvelopePayload]`-carrying envelopes whose staged payload bytes push the outer writer past its cap.
- Generated serializers are stateless, so a mid-write failure leaves the serializer instance fully reusable — the same instance round-trips the next message with no cleanup.
- The writer is reusable after `Reset()`: the transport's dead-letter-then-reuse pattern (translate the exception into a dead-letter for that send, reset the pooled writer, encode the next message into the same buffer) works without re-renting.
- The written count never exceeds the cap after a failure, so the encode-time boundary is hard, not advisory.

Exact generated `SizeHint` (task 5.12, extended to formatter-backed fields by Decision 11) is the complementary happy-path tool: callers can pre-size `initialCapacityHint` from `SizeHint` so the exception path is reserved for genuinely oversized messages rather than being a common-case growth mechanism.

## Risks / Trade-offs

**Generator complexity**: keep diagnostics focused and add incrementally.

**MessagePack conventions**: document DateTime, Guid, decimal, nullable, collection, and nested object conventions.

**Benchmark interpretation**: the first benchmark is directional POC evidence, not final Artery performance proof.

**API churn**: if sourcegen finds V2 API problems, fix V2 before Artery starts.

**Persistence compatibility**: generated serializers must not compromise stored payload readability.
