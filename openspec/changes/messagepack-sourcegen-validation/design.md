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
[AkkaSerializer<IOrderProtocol>("orders", 120001)]
public sealed partial class OrderSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
```

`Name` and `SerializerId` are required, positional, get-only constructor arguments on
`[AkkaSerializer<TProtocol>]` -- there is no named-property form and no auto-assignment (Decision
14). The base class is the non-generic `AkkaSerializer`; the protocol marker interface is carried
entirely by the attribute's type parameter (Decision 15).

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

**As implemented for 1.6**, the generator natively recognizes all ten collection shapes originally scoped: `T[]`, `List<T>`, `IReadOnlyList<T>`, `Dictionary<TKey,TValue>`, `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>`, `IReadOnlyCollection<T>`, and `IReadOnlyDictionary<TKey,TValue>` (`AkkaSerializerGenerator.TryMapCollection`, `TryMatchSingleArgumentKind`, `TryMatchKeyValueKind`; round-trip and wire-format coverage in `CollectionFieldSpec.cs` and `ImmutableCollectionFieldSpec.cs`). Every shape shares IDENTICAL MessagePack wire framing -- an `ImmutableList<int>` field is byte-for-byte identical on the wire to the same data in a `List<int>` field, and likewise `ImmutableArray<int>` vs `int[]` -- only the in-memory construction on deserialize differs per shape:

- `IReadOnlyList<T>` and `IReadOnlyCollection<T>` deserialize to a concrete `List<T>`; `IReadOnlyDictionary<TKey,TValue>` deserializes to a concrete `Dictionary<TKey,TValue>` (same reasoning as `Dictionary<TKey,TValue>` itself).
- `ImmutableList<T>`, `ImmutableHashSet<T>`, and `ImmutableDictionary<TKey,TValue>` deserialize via the type's own `Builder` (no capacity parameter -- these are tree/trie-backed, not array-backed) followed by `.ToImmutable()`. A duplicate key overwrites (last-write-wins, matching `Dictionary<TKey,TValue>`'s own indexer semantics); a duplicate `ImmutableHashSet<T>` element is silently deduplicated (matching a normal set's `Add`).
- `ImmutableArray<T>` deserializes via `ImmutableArray.CreateBuilder<T>(capacity)` pre-sized to the wire's element count, followed by `Builder.MoveToImmutable()` -- a zero-copy handoff instead of a defensive copy, since `Count` always equals `Capacity` when the read loop finishes.

`ImmutableArray<T>` is the one VALUE-typed (struct) collection shape; every other shape is a reference type, where "null" is a genuine CLR null and the existing null-vs-empty wire distinction (nil vs a zero-length array/map header) applies unchanged. `ImmutableArray<T>` cannot be compared to `null` (`value is null` does not compile against a non-nullable value type), and on current .NET (verified against the in-box `System.Collections.Immutable` on net10.0) accessing `.Length` or enumerating a `default(ImmutableArray<T>)` throws `NullReferenceException` -- so every code path touching an `ImmutableArray<T>` value must check `.IsDefault` before doing anything else with it. The implementation maps `ImmutableArray<T>`'s own `IsDefault`/`Empty` distinction onto the SAME nil-vs-empty wire framing every other collection shape already uses: `default(ImmutableArray<T>)` (`IsDefault` true) writes as MessagePack nil and reads back as `default`, exactly mirroring how `null` round-trips for a reference collection; `ImmutableArray<T>.Empty` (`IsDefault` false, `Length` 0) writes as a zero-length array header and reads back as a fresh, non-default empty array, staying distinct from the nil case on the wire and after deserialization. A REQUIRED (non-nullable) `ImmutableArray<T>` field therefore accepts a nil-encoded wire value without throwing -- `default(ImmutableArray<T>)` is a legal value of a value type, exactly as `0` is a legal value for a required `int` field, unlike a required reference-collection field, which rejects a nil value as "missing." `ImmutableArray<T>?` (`Nullable<ImmutableArray<T>>`) composes through the generator's existing nullable-value-field machinery; both `HasValue == false` and `HasValue == true` with an internally-default array collapse to the same wire nil and both read back as `HasValue == false`.

Collections compose (nested `List<List<int>>`, jagged arrays, dictionaries of lists, `ImmutableList<ImmutableArray<int>>`, `ImmutableDictionary<string, List<int>>`, collections of nested `[AkkaSerializable]` types), and an unsupported element/key/value type collapses the whole field to `FieldKind.Unsupported` (AKKASG003) rather than partially generating -- for every one of the ten shapes, not just the original four. Collection shapes outside this ten-shape scope (`HashSet<T>`, `ImmutableSortedSet<T>`, `ImmutableQueue<T>`, etc.) remain AKKASG003, by design.

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

The generator's nested-value-object rule (Decision 7.1: every nested field type must carry `[AkkaSerializable]` and its own explicit `[AkkaField]` schema) fails closed for types the generator cannot annotate. Core Akka types such as `Akka.Actor.Address` are the canonical case: `[AkkaSerializable]` lives in `Akka.Serialization.V2`, which references `Akka` — annotating `Address` directly would require a dependency cycle. Before this decision, a serializer with an `Address`-typed field (nested or top-level) always failed compilation with `AKKASG007` (`MissingNestedSerializableDefinition`), with no escape hatch. This is exactly the friction that forced Artery's handshake messages (`HandshakeReq`/`HandshakeRsp`, which carry `UniqueAddress`/`Address`) onto a hand-rolled base-class subclass (`Akka.Remote.Artery.ArteryControlMessageSerializer`, at the time named `MessagePackSerializer<T>` -- see Decision 15 for the later rename to `AkkaSerializer`) instead of a generated one.

Two options were evaluated:

1. **Relocate or metadata-match `[AkkaSerializable]` so core types can opt in.** Either move the attribute to a dependency-free assembly core Akka can reference, or have the generator match attributes by metadata name/shape across assemblies instead of a single compile-time attribute reference. **Rejected.** The generator is syntax-driven (`ForAttributeWithMetadataName` on the *current* compilation's syntax trees): `[AkkaSerializable]` types declared in a *referenced* assembly (like core `Akka`) are invisible to it regardless of where the attribute type lives, because there is no syntax node to walk in the referencing compilation. Making that work would require a metadata-based schema-extraction redesign (reading previously-generated schema facts back out of referenced assemblies), which is a much larger change than the problem justifies. It also permanently couples core Akka types to a durable generated-wire schema the moment they're annotated, and it still cannot express context-dependent encodings — `IActorRef` and `ActorPath` fields need transport-aware address substitution (`Serialization.SerializedActorPath`, `ActorPath.ToSerializationFormatWithAddress`) that no static per-type schema can capture.

2. **A per-serializer formatter escape hatch.** **Chosen.** A serializer opts a specific foreign type into hand-written encoding via `[AkkaSerializerFormatter<TTarget, TFormatter>]`, where `TFormatter` implements `Akka.Serialization.V2.IAkkaMessagePackFormatter<TTarget>`:

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

   Formatters are constructed once per generated serializer instance, in the generated constructor, using either a public parameterless constructor or a public constructor taking `Akka.Actor.ExtendedActorSystem` (for formatters that need system context, e.g. to resolve local vs. remote encoding). When BOTH constructors are present, the generator prefers the `ExtendedActorSystem` one: the generated serializer always has the system in hand, and system context is why a formatter declares that constructor in the first place — silently picking parameterless would drop it. Registering a formatter that exposes neither usable constructor shape fails compilation (`AKKASG010` no usable constructor). `TFormatter`'s interface conformance to `IAkkaMessagePackFormatter<TTarget>` is no longer a generator diagnostic at all: the `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint on the now-generic `[AkkaSerializerFormatter<TTarget, TFormatter>]` attribute (Decision 15) makes a non-conforming formatter a compiler error at the attribute usage site. `AKKASG008` narrows to the one thing that constraint cannot express — `TFormatter` must not be abstract (there is deliberately no `new()` clause, since an `ExtendedActorSystem`-only constructor is legitimate) — instead of silently falling back to the old nested-object behavior. Registering two formatters for the same target type on one serializer also fails compilation (`AKKASG009`), as does registering a target type that is not a plain named type — arrays and closed generics (`AKKASG011`; an open generic can no longer reach this check either, since C# does not allow an unbound generic type as an attribute type argument) — rather than the registration silently doing nothing or colliding on the arity-less type name the generator uses for field matching.

   Two built-in formatters ship in `Akka.Serialization.V2`: `AddressFormatter` for `Akka.Actor.Address`, and `ActorPathFormatter` for `Akka.Actor.ActorPath`. `AddressFormatter`'s wire format is deliberately **byte-identical** to `ArteryControlMessageSerializer`'s hand-rolled `WriteAddress`/`ReadAddress`/`SizeOfAddress` (a 4-element array of `[Protocol, System, Host-or-nil, Port-or-nil]`), so a generated serializer that registers `[AkkaSerializerFormatter<Address, AddressFormatter>]` can read and write the exact bytes Artery's control-message serializer already produces on the wire today. `ActorPathFormatter` writes a single transport-aware string using the same convention the generator already uses for `IActorRef` fields: it reads the thread-static transport context (`Serialization.CurrentTransportInformation`, accessed directly via an `InternalsVisibleTo` grant to `Akka.Serialization.V2` so no exception is thrown or caught on the non-transport path) and renders the path with the transport's address via `ActorPath.ToSerializationFormatWithAddress` when one is set; outside any transport scope it falls back to the owning system's `Provider.DefaultAddress` when constructed with an `ExtendedActorSystem` (which generated serializers do automatically, since the generator prefers that constructor — matching `Serialization.SerializedActorPath` semantics, so the path stays remotely resolvable), and only to `ActorPath.ToSerializationFormat()` when it has no system at all. Because `SizeOf` and `Write` each read the thread-static context independently, transport-sensitive formatters require both calls to run under the same transport scope/thread for the exact-size contract to hold — the generated serializers and the Artery encode path do this naturally. This closes the loop that started `ArteryControlMessageSerializer`'s hand-rolled fallback: a generated serializer can now reproduce that exact wire format, so the hand-rolled class is a candidate for replacement by a generated one in a follow-up change, without a wire-format break.

   As a related but independently useful fix in the same change, the generator now emits the serializer partial class with the **declared accessibility of the user's serializer symbol** (`public` or `internal`) instead of hardcoding `public`. This has no direct dependency on the formatter escape hatch, but it was required to exercise `[AkkaSerializerFormatter]` against `internal` serializers (the shape Akka.Remote's own control-message serializer would need if it were later migrated to the generator), and there was no reason to gate it behind a separate change.

### 12. Oversized-Payload Determinism

Oversized-payload failure is deterministic and happens at encode time. The contract: a caller-imposed cap — a transport's maximum-frame-size expressed as `PooledPayloadWriter.maxCapacity` — makes any payload whose encoding would exceed that cap fail *during* `Serialize` with a typed `Akka.Serialization.PayloadSizeExceededException` carrying the attempted size and the configured cap. A truncated or corrupt frame is never observed downstream: the writer refuses the write that would cross the boundary, so no partial frame larger than the cap ever exists to hand to a transport.

The writer-side mechanism is serializer-v2 design.md Decision 12 (`PooledPayloadWriter` + ownership contract): every `GetSpan`/`GetMemory`/`Advance` that would push the written count past `maxCapacity` throws, and writer mechanics are covered by `Akka.Tests.Serialization.PooledPayloadWriterSpec`. What this change pins (`OversizedPayloadDeterminismSpec`, task 6.8) is the *serializer-side* half of the contract:

- The exception propagates out of the generated serializer's `Serialize` call, for both plain generated messages and `[AkkaEnvelopePayload]`-carrying envelopes whose staged payload bytes push the outer writer past its cap.
- Generated serializers are stateless, so a mid-write failure leaves the serializer instance fully reusable — the same instance round-trips the next message with no cleanup.
- The writer is reusable after `Reset()`: the transport's dead-letter-then-reuse pattern (translate the exception into a dead-letter for that send, reset the pooled writer, encode the next message into the same buffer) works without re-renting.
- The written count never exceeds the cap after a failure, so the encode-time boundary is hard, not advisory.

Exact generated `SizeHint` (task 5.12, extended to formatter-backed fields by Decision 11) is the complementary happy-path tool: callers can pre-size `initialCapacityHint` from `SizeHint` so the exception path is reserved for genuinely oversized messages rather than being a common-case growth mechanism.

### 13. Manifest-Discriminated Unions And Closed Generic Registrations

Two structural shapes were added after the decisions above, both following the closed-polymorphism precedent `System.Text.Json`'s source generator already established (`[JsonDerivedType]`, `[JsonSerializable]` on closed constructions).

**Unions.** `[AkkaUnion(Type first, params Type[] rest)]` declares a closed, explicitly-enumerated set of concrete `[AkkaSerializable]` member types, either on the union's base interface/abstract class (type-level, inherited by every field of that static type) or on one `[AkkaField]` property (a narrowing override for that field only). The wire format is a manifest-discriminated 2-entry map, `{1: member manifest, 2: inline member field map}` — deliberately distinct in shape from the `[AkkaEnvelopePayload]` frame (`{1: serializerId, 2: manifest, 3: opaque bytes}`), because a union member is encoded structurally inline at compile time, not resolved through runtime Akka serializer lookup. Write dispatch matches the exact runtime type: a value whose exact type is not a declared member — including an undeclared subtype of a declared member — fails serialization rather than silently widening. Because that failure mode is specifically about undeclared *subtypes*, the generator emits an advisory (`AKKASG025`, `DiagnosticSeverity.Info`, not an error) when a union member type is not sealed, rather than blocking compilation over a shape that is often intentional. Dispatch helpers are deduplicated by (field static type, member set) identity, so the common case — one type-level `[AkkaUnion]` declaration reused by many fields — emits one helper, not one per field.

**Closed generic registrations.** A Roslyn source generator is syntax-driven and cannot reify an open generic type, so a generic `[AkkaSerializable]` type (e.g. `Wrapper<T>`) is never itself serialized — the open definition exists only to host the `[AkkaField]` schema its closed constructions share. `[AkkaSerializable<Wrapper<IOrder>>(Manifest = ...)]`, applied to the `[AkkaSerializer]` class, registers one closed construction; each registration then behaves exactly like an ordinary top-level message, with its own manifest and dispatch arm, and its generic fields resolved against the concrete type arguments. A generic definition that implements the serializer's protocol interface with no registered closed construction fails compilation (`AKKASG022`), and a field typed as an unregistered closed construction fails compilation (`AKKASG023`) — the source-generation analog of System.Text.Json rejecting an unbound generic in `[JsonSerializable]`.

Scope differs between the two features, and `CrossAssemblyBaselineSpec` pins the current behavior. A union member type declared only in a referenced assembly is invisible to this generator. It fails AKKASG015 even when it carries `[AkkaSerializable]` and a manifest in that assembly. A closed-generic registration is not scoped that way. `[AkkaSerializable<T>]` resolves the generic definition through symbol APIs, not through the per-compilation syntax registry that backs the union check. So a closed construction over a generic definition that lives only in a referenced assembly (`Wrapper<T>` in A, `[AkkaSerializable<Wrapper<int>>]` on B's serializer) compiles and generates correctly today. What fails across assemblies is a non-generic `[AkkaSerializable]` type used as a nested field, and a union member declared in a referenced assembly. Before this branch the nested-field case was misreported as AKKASG023 (unregistered closed generic). It now reports AKKASG007, and both diagnostics name the type, its assembly, and the fixes that work today.

### 14. Always-Explicit Serializer Identity

`[AkkaSerializer<TProtocol>(string name, int serializerId)]` requires both constructor arguments at every call site — there is no auto-assigned id or alias, and no named-property form. Two properties of source generation make auto-assignment unsafe rather than merely inconvenient: a generator's registration order across a compilation is nondeterministic (so a stable auto-id cannot be derived from declaration order), and a collision with a serializer id or alias defined elsewhere — for example in HOCON `akka.actor.serialization-bindings` / `serializers` — is invisible to the generator at compile time, so it cannot even validate the id it would pick. `AKKASG001`/`AKKASG002` accordingly guard only argument *validity* (non-null/empty/whitespace name; positive id), not presence, since the compiler now makes omission impossible.

### 15. Illegal-States-Unrepresentable Attribute Pass

A design review (issue #8384, PR #8385) reshaped the attribute surface so several states the generator previously rejected with a diagnostic became states the C# compiler rejects outright, and added generator-level validation of the `[AkkaSerializer]` class shape itself that had no coverage before:

- The base class dropped its `TProtocol` type parameter — `MessagePackSerializer<TProtocol>` became the non-generic `AkkaSerializer` — moving protocol identity entirely onto `[AkkaSerializer<TProtocol>]` and eliminating a shape where a serializer's base-class type argument and its attribute's type argument could silently disagree. (The rename also sidesteps a `CS0104` ambiguity against `MessagePack.MessagePackSerializer` under dual top-level usings.)
- `[AkkaUnion]`'s constructor became `(Type first, params Type[] rest)` (Decision 13), so `[AkkaUnion()]` — an empty member set — no longer compiles; `AKKASG019` keeps only the duplicate-member-type half of what it used to guard.
- `[AkkaSerializerFormatter<TTarget, TFormatter>]` became generic with a `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint (Decision 11), so non-conformance to the formatter interface is now a compiler error at the attribute usage site rather than an `AKKASG008` diagnostic.
- The generator now validates the `[AkkaSerializer]` class shape itself — partial, non-generic, derives from `AkkaSerializer` (`AKKASG032`) — that its protocol type argument is an interface (`AKKASG033`), that no two serializers bind the same protocol (`AKKASG031`), and that every type implementing a bound protocol is `[AkkaSerializable]` (`AKKASG029`). Each of these closes a gap where the previous shape either compiled into a silently-empty dispatch switch or failed only at runtime on first send.
- Diagnostic message text and hand-written runtime code stopped using `global::`-qualified type names in favor of human-readable names (readability only; no behavior change).

### 16. Schema Extraction From Referenced-Assembly Metadata

A message in serializer B can have a field whose type lives in assembly A. The generator in B reads that type's schema from A's compiled metadata. It reads the attributes, the properties, and the constructor. It emits private `Write`, `Read`, and `SizeOf` helpers inside B's own serializer. This covers three uses of a type from a referenced assembly:

- the type of a nested field
- a member type of a union
- the generic definition behind a closed-generic registration, from Decision 13

Local declarations keep priority. A type declared in the current compilation behaves as before. The closed-generic path already extracts schema this way today. This decision extends that technique to nested fields and union members.

A's metadata already has everything the generator needs:

- the serializable attribute and its manifest
- the field attributes and their indexes
- the property types
- the constructor shape

The generator builds the codec inside B, not inside A. So A needs no serializer. A needs no reference to the source generator. Nothing couples A and B at run time. The only calls across the boundary are normal constructor and property calls, the kind any code makes on any type.

Two alternatives were rejected. The first is a public generated per-type contract. A generates a public formatter for each of its types. B calls that formatter. This cannot handle closed generics, because A cannot know which constructions B will register. It also ties A and B together at the code level. The second is a DTO mirror. B declares its own copy of each A type. B marks the copy serializable. B converts between the two by hand. Akka's own internal migration code does this today, for a handful of types. It does not scale to a customer with hundreds of types.

A type from a referenced assembly must carry `[AkkaSerializable]`. It must also be accessible from B: public, or visible through `InternalsVisibleTo`. A type that has the attribute but is not accessible from B fails compilation. This is a new diagnostic, distinct from the existing missing-attribute diagnostic.

Assembly A never gets a diagnostic. No generator work runs there. Every failure reports at the local reference site in B. That site is the property that names the type from A, or the registration attribute for a closed generic. The message names the upstream type and its assembly. The failure can sit one level down, inside A's own schema. Then the message also names the failing member of the A type. It does not stop at the property in B that referenced it. Every such message offers both fixes. Fix one: add `[AkkaSerializable]` to the type in A. That type then needs to reference `Akka.Serialization.V2`. Fix two: register a hand-written `[AkkaSerializerFormatter]` on B's serializer for that type.

Two serializers in two different assemblies can both nest the same A type. Each one generates its own private copy of the codec. The copies are byte-identical by construction. Both read the same attributes from the same metadata. Both emit from the same extraction routine. There is no shared runtime codec to keep in sync. There is only independently generated code that happens to agree.

Characterization facts about today's behavior live in `CrossAssemblyBaselineSpec`, on branch `feature/serialization-v2-cross-assembly-baseline`:

- a nested field type from a referenced assembly failed with a mislabeled AKKASG023, "closed generic must be registered." That branch changes it to AKKASG007, with a hint that names the type, its assembly, and the fixes that work before this decision ships
- a union member from a referenced assembly fails with AKKASG015, with the same hint
- a generic definition behind a closed-generic registration already works today. This decision does not change that case

### 17. Unknown Union Members: The Generator Throws, The Caller Decides

This decision closes the two "to be designed" notes in issue #8384. Note 1 proposed an envelope fallback on write, for a union value whose runtime type is not a declared member. This is rejected. Note 2 proposed skip-or-null on read, for an unknown union manifest. This is rejected too. On write, if the runtime type is not a declared member, the generated code throws `SerializationException`. On read, if the manifest does not match a declared member, the generated code throws `SerializationException`. The generator adds no fallback. The generator adds no opt-in, for either case.

The wire already decides where a message goes. Nothing in that path leaves room for a fallback. The receiver dispatches on the serializer id, then on the manifest. It can never choose a different serializer or a different type. A union frame does not even carry a serializer id. The enclosing serializer owns every member. So the generated manifest switch is the only dispatch point that exists. An envelope fallback on write would add a second wire shape in the same slot. The union would have to stay compatible with that shape forever. It would also need a serializer binding. That brings back the runtime lookup the union exists to remove. Skip-or-null on read causes silent data loss, on any field the schema marks as required.

Read dispatch has three steps. Each step either matches or throws. First, the serializer id on the wire selects one serializer. An unknown id throws "Cannot find serializer with id." Second, the generated `FromBinary` manifest switch selects one message type. Third, inside a union field, the union frame's own manifest selects one declared member. An unknown manifest throws inside the generated reader. This is the same way a hand-written string-manifest serializer throws, on a manifest it has no case for.

A new field on an existing message or union member is a different problem. It already works today:

- the reader skips field ids it does not recognize
- a missing nullable field is allowed
- a missing non-nullable field throws "Missing required field"

The rule for a new field is to make it nullable. A new member type added to a union is different. That mechanism does not cover it. An old build has no case for a manifest it has never seen. Skipping keys cannot produce a type that does not exist in that build. The rule for a new member type is the same as for a new top-level message: deploy before send. For persisted messages, deploy before write. An old build cannot recover an event it cannot deserialize.

After the exception leaves the generated serializer, the caller decides what happens next. None of these callers get a new rule. Classic remoting fails the association on an inbound deserialization error. Classic remoting keeps the association on an outbound error. Persistence fails recovery.

### 18. Closed-Set Expansion And Adoption On The Serializer

A registration can have a type argument with a closed set. The closed set can be the serializer's own protocol interface. The closed set can be any type carrying a type-level `[AkkaUnion]`. Such a registration expands to one closed construction per member of that set. It does not register only the single construction named in the attribute. For example, take `[AkkaSerializable<Envelope<ICommsMessage>>(ManifestPrefix = "env")]` on a serializer whose protocol is `ICommsMessage`. It registers `Envelope<AcceptCassette>`, `Envelope<OrderCancelled>`, and one construction per remaining member of the protocol set. Each construction gets its own dispatch arm, private helpers, and manifest.

The registration attribute gains a new property: `ManifestPrefix`, alongside the existing `Manifest`. The two properties combine into four cases:

- `Manifest` alone registers exactly the named construction, with no expansion. This is today's behavior.
- `ManifestPrefix` alone expands over the closed set. It does not register the literal construction.
- both together register the literal construction under `Manifest`, and the expansion under `ManifestPrefix`
- neither property present is still AKKASG006, the existing manifest-required diagnostic

`ManifestPrefix` on a construction whose type argument is a concrete class is an error. A concrete class has no set to expand.

The manifest for each expanded construction comes from a fixed formula. The generator evaluates it once. Both sides of the wire produce the same result:

```
manifest(explicit registration, Manifest = "m")   = "m"
manifest(M) for a member of the set               = the top-level manifest of M
manifest(G<M>, ManifestPrefix = "p")              = "p" + "/" + manifest(M)
manifest(G<M1, M2>, ManifestPrefix = "p")         = "p" + "/" + manifest(M1) + "/" + manifest(M2)
manifest(G<H<M>>, ManifestPrefix = "p")           = "p" + "/" + manifest(H<M>)
```

Every member of a closed set already has its own manifest. AKKASG006 requires one on a top-level message. The equivalent union rule requires one on a union member. The nested case, `G<H<M>>`, needs `H<M>` itself registered separately. Only then does it have a manifest to compose into the outer formula. Both sides of the wire emit the same literal strings, from the same formula, over the same set. The runtime never builds or parses a manifest.

A field whose static type is the serializer's own protocol interface is a union over the protocol set. No `[AkkaUnion]` attribute is required. The protocol set is already the closed set. This rule is what makes the literal construction encodable at all. The literal construction is the one registered under a bare `Manifest`, with the interface as its type argument. Its payload field is typed as the interface. The generator treats that field as a union over the same set every expanded construction draws from.

The expansion rule applies to every type argument of a registration, not only the first. A multi-argument generic expands to the product of its arguments' sets. A type argument that is itself a generic with its own closed-set argument expands recursively. The number of generated constructions can grow quickly. So the generator reports it as an info diagnostic. A broad expansion stays visible instead of turning into a silent pile of generated code.

An explicit registration for one specific construction, with its own `Manifest`, stays valid alongside an expansion. It overrides the manifest the formula would have derived for that construction. This lets a team pin or migrate a manifest for a single message, without touching the rest of the expansion. It also remains the only way to register a construction whose type argument is a concrete class. A concrete class has no set to expand over.

A registration attribute on the serializer class no longer means only "register this closed generic construction." It means adopt this type into this serializer, generic or not. AKKASG034, the "registration has no effect" diagnostic, is retired. AKKASG020 stops rejecting a non-generic type argument. Every construction adopted this way gets a dispatch arm, private helpers, and a concrete binding in the generated registration. This holds whether the construction implements the serializer's protocol or not. The runtime can route a value of that exact type to this serializer, even when the type implements nothing in particular. Three constraints remain, checked at build time:

- the adopted type must carry `[AkkaSerializable]` and its field schema, wherever it lives, including a referenced assembly under Decision 16
- it needs a manifest, from its own attribute or from the registration's `Manifest` override
- one type may have only one owner. A second serializer adopting the same type is a conflict. So is a type that implements a protocol another visible serializer owns, and is separately adopted. This is a build error when the two serializers can see each other. It is a startup check when they cannot see each other. Decision 19 covers that check.

Anything the generator cannot see at build time does not exist. A construction can be built through `Activator.CreateInstance` and `MakeGenericType`, over a type argument outside the registered set. That construction has no matching case. It fails at send time with an `ArgumentException` naming the unsupported type. Reflection is not a path the generator accommodates. This rule closes a real gap. A generic type declared in Core.dll cannot implement a protocol interface declared in Comms.dll. Core.dll does not reference Comms.dll. So a closed construction of that generic fails the old test. The old test requires the type to implement the protocol, or be reachable from a field. Today that gap surfaces as AKKASG034. As pinned in `CrossAssemblyBaselineSpec`, on branch `feature/serialization-v2-cross-assembly-baseline`, that one diagnostic also suppresses emission of the entire serializer. One stray registration then turns into a pile of unrelated missing-partial compile errors.

### 19. Protocol Implementors From Referenced Assemblies, And Serializer Placement

The generator finds top-level messages for a protocol not only in the current compilation. It also walks every referenced assembly that itself references `Akka.Serialization.V2`. For each such assembly, it enumerates types from metadata. It adopts every `[AkkaSerializable]` implementor of the protocol interface it finds. No attribute is needed to opt an assembly in. No runtime type scanning happens. No scanning of how or whether a type is actually used happens either. This amends Decision 16. Top-level dispatch, not only nested-field and union-member resolution, covers this compilation plus every referenced assembly that references V2.

The walk's output is a sorted list of fully qualified type names. It is a plain value-equatable list, not a set of symbols. So the incremental generator's caching stays intact once that list stops changing. Every later stage keys off the list, not off the walk itself.

AKKASG029, a local implementor of the protocol missing `[AkkaSerializable]`, and AKKASG012, manifest uniqueness, widen to run over this combined, cross-assembly set. They no longer run only over the current compilation's types. An implementor found in a referenced assembly without the attribute is reported on the serializer class itself. There is no local property or declaration in this compilation to attach the diagnostic to.

This has two costs, each with a mitigation. First, the message set a serializer owns changes whenever someone adds a project reference. Nobody explicitly assigns the new messages to that serializer. This is by design. The construction-count info diagnostic from Decision 18 makes a resulting jump in scope visible, rather than silent. Second, two serializers declared in assemblies that do not reference each other can each adopt the same implementor. Neither compilation can see the other, to object. Where one serializer's assembly can see the other's, the cross-assembly AKKASG031 check catches the collision at build time. That check enforces one protocol, one serializer. Where neither can see the other, a startup check catches it instead, when registrations are composed. That check throws, naming both serializers.

The constraint behind all of this is not circular. It does not require a serializer's identity and its message encoders to live in the same assembly. A compilation cannot see assemblies that depend on it. So the code that encodes a message must be generated in a compilation that can see that message. That compilation is the message's own assembly, or one below it. Nothing in that requirement says the serializer's name, id, and protocol declaration must sit in that same compilation.

Splitting serializer identity from its encoders is a documented follow-up, not part of this change. A serializer identity is declared upstream, with no messages of its own. A generated "part" holds each downstream assembly's encoders. Composition happens explicitly at startup, one line per part, with no scanning. This remains available for the case where a serializer's identity must sit above its messages. The shapes this change targets do not need it.

Four placement rules turn a misplaced serializer into a compile-time or startup failure, instead of a silent gap. A downstream compilation reports an error on the type declaration. This happens when a type implements a protocol that an upstream serializer already binds. That upstream assembly can never see this type, so it would never be dispatched. An upstream compilation reports a warning on the serializer class. This happens when a serializer has no messages in its own compilation or in any referenced assembly, and also has no registrations. A serializer with nothing to serialize is very likely a placement mistake. AKKASG031 extends across assemblies. Two serializers that bind the same protocol collide at build time, whenever one assembly can see the other. The runtime "unsupported generated serializer type" exception text improves too. It now names both the type's assembly and the generating serializer's assembly, not just the type.

A message assembly whose serializer lives in a host assembly below it is not an error condition. That compilation sees no serializer at all, which is a legitimate shape. It gets at most an info diagnostic, rather than a warning or error.

### 20. The Envelope Payload Attribute Is Removed: object Is The Boundary

`[AkkaEnvelopePayload]` is removed. A property typed `object`, or `object?`, is now the serializer boundary. The generator writes it as the envelope frame: serializer id, manifest, bytes. The static type of the property selects the encoding. Nothing else does.

The rule runs on the static type, after generic substitution:

- `object` selects the envelope frame
- the serializer's own protocol interface, or a type carrying a type-level `[AkkaUnion]`, selects a union frame
- any other interface or abstract class, one with no closed set, is `AKKASG003`. The hint reads: declare `[AkkaUnion]`, use the protocol interface, or type the property as `object`
- a concrete serializable type is encoded inline

A generic wrapper follows its type argument. `Envelope<object>` is an envelope frame. `Envelope<IComms>` is a union frame. `Envelope<AcceptCassette>` is inline.

The attribute only ever added a static type to a boundary field. The static type alone can carry that meaning. On `dev`, eight usages exist in product code and tests:

- four are `object` or `object?` already
- two are interfaces: the Artery system-message DTO in `SystemMessageDeliveryMessages.cs`, and one spec
- two are concrete envelope types, in nesting tests

The reliable-delivery branch adds four more, in its own serializer. Every one of these is a DTO mirror or a test fixture. Retyping each to `object`, and casting at the consumer, costs almost nothing. The principle matches Decisions 18 and 19. The closed set comes from the type. The open case is explicit, because the author wrote `object`.

This change touches five things:

- one attribute class is removed. Six remain
- `AKKASG035` is retired. The conflict it guarded against can no longer occur
- `AKKASG003` gains the hint text above
- the envelope nesting-depth guard stays as it is
- the Artery DTO and the reliable-delivery serializer both retype their boundary fields to `object`

The API is unreleased. So this needs no entry in the breaking-changes ledger. It must land before the first 1.6 beta. After that beta ships, the same change would be a breaking change.

This ships as its own small PR, independent of the others.

### 21. Unions Without A Member List

`[AkkaUnion]` with no arguments, on an interface or an abstract class, gets a new meaning. The closed set becomes every `[AkkaSerializable]` implementor the generator can see. The explicit list, `[AkkaUnion(typeof(A), typeof(B))]`, still works. As a field-level override, it works anywhere in the serializer's compilation. As a type-level form, it works only where every listed member is visible from the type's own assembly.

Three reasons back this decision.

- Assembly direction causes the first problem. In a layered codebase, the interface lives upstream of its implementations. An assembly cannot name a type from an assembly that references it. So a type-level member list is uncompilable there, not merely tedious.
- Intent stays explicit under this rule. The marker declares the interface a wire contract. Without a marker, the generator would have to walk implementors of any interface a field mentions. It could not tell a wire contract from an incidental interface with in-memory-only implementations.
- The third reason is cost. This decision reuses Decision 19's mechanism and cost model. It only points that mechanism at a marked interface, instead of the protocol interface. The protocol interface needs no marker. Decision 18 already covers it, because `[AkkaSerializer<TProtocol>]` declares the protocol.

This decision changes six things:

- the attribute regains a parameterless constructor, with a documented meaning: all visible serializable implementors, not an empty set. Decision 15 removed the empty form. This is a different meaning
- an implementor of a marked interface without `[AkkaSerializable]` is a build error. This is the same rule as AKKASG029 for the protocol. It reports at the serializer class when the implementor lives in a referenced assembly
- the empty-set half of AKKASG019 does not apply to the marker form
- the set is scoped as in Decision 19: this compilation, plus referenced assemblies that reference V2
- both costs of Decision 19 apply, with the same mitigations: the construction-count diagnostic, and the startup one-owner check
- the wire format does not change. A union frame carries each member's own manifest, whether the set was listed or found

This ships with the referenced-assembly walk from Decision 19. It is the same walk.

## Risks / Trade-offs

**Generator complexity**: keep diagnostics focused and add incrementally.

**MessagePack conventions**: document DateTime, Guid, decimal, nullable, collection, and nested object conventions.

**Benchmark interpretation**: the first benchmark is directional POC evidence, not final Artery performance proof.

**API churn**: if sourcegen finds V2 API problems, fix V2 before Artery starts.

**Persistence compatibility**: generated serializers must not compromise stored payload readability.
