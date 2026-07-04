## Context

Akka.NET currently centers serialization around `Serializer` and `SerializerWithStringManifest` in `src/core/Akka/Serialization/Serializer.cs`. The primary API is `ToBinary(object) -> byte[]` and `FromBinary(byte[], Type/string) -> object`. This forces byte-array allocation even when the caller could write directly to a buffer or deserialize from a `ReadOnlySequence<byte>`.

The immediate blast radius of changing this API is larger than core Akka:

- Classic Akka.Remote uses `MessageSerializer` and `WrappedPayloadSupport` to place serializer ID, manifest, and payload bytes into protobuf envelopes.
- Akka.Persistence stores nested payloads in protobuf `PersistentPayload` records containing serializer ID, manifest, and payload bytes.
- Snapshot storage uses wrapper serializers directly.
- DistributedData and Akka.Delivery have direct serializer call sites.
- Tests frequently assert concrete serializer types returned from `FindSerializerForType()`.

This means `SerializerV2` must be introduced with the classic remoting and persistence bridges in the same change. A V2-only core change would leave the repo in a partially broken state.

## Goals / Non-Goals

**Goals:**

- Introduce `SerializerV2` as the canonical Akka.NET 1.6 serialization abstraction.
- Keep public `FindSerializerFor()` / `FindSerializerForType()` compatible while adding internal V2 lookup and buffer-first APIs.
- Keep V1 serializers working through `SerializerV1Adapter`.
- Require new native V2 serializers to emit non-empty, non-CLR manifests to avoid polymorphic deserialization, while preserving legacy manifests for existing serializer IDs.
- Preserve classic Akka.Remote wire compatibility.
- Preserve Akka.Persistence stored event and snapshot compatibility.
- Decide V2 API details needed by sourcegen and Artery before either depends on the API.
- Keep MessagePack and source generation out of core Akka.

**Non-Goals:**

- Implementing source-generated MessagePack serializers.
- Introducing Artery envelopes or Artery TCP.
- Replacing classic remoting wire format.
- Rewriting all internal protobuf serializers to MessagePack.
- Removing V1 serializer classes.

## Decisions

### 1. SerializerV2 Is Canonical With A Transitional Compatibility Surface

`SerializerV2` is the canonical 1.6 serializer abstraction and exposes buffer-first serialization through `IBufferWriter<byte>` plus deserialization from `ReadOnlySequence<byte>`.

For the foundation PR, `SerializerV2` remains usable through existing `Serializer` call sites so classic remoting, persistence, and public lookup APIs can stay compatible while internals move to V2. V1 compatibility is provided by `SerializerV1Adapter : SerializerV2`.

Rationale: a hard public API break at this layer creates broad compatibility fallout before any native V2 serializers exist. The compatibility inheritance is transitional design debt; it should be revisited before native V2 serializers become widespread.

### 2. V2 Still Provides Bridge Methods

`SerializerV2` should expose compatibility bridge methods such as `ToBinary` and `FromBinary` so existing code paths can be migrated incrementally.

These bridge methods should be implemented in terms of the V2 buffer API for native V2 serializers and delegated to the inner serializer for V1 adapters.

Rationale: classic remoting and persistence still need byte arrays at protobuf boundaries. Bridge methods keep those compatibility paths clear while new internals can use buffer-first V2 APIs.

### 3. Serialize Must Report Bytes Written

The V2 serialize API must make frame and payload length accounting explicit.

The exact shape can be `int`, `ValueTask<int>`, or a small result type, but callers must not have to infer bytes written from unrelated state when building envelopes.

Rationale: Artery envelopes and frame encoders need accurate payload length accounting. Classic remoting and persistence can ignore this in bridge paths, but the API must be suitable before sourcegen and Artery work begins.

### 4. SizeHint Needs Unknown Size

`SizeHint` must support an unknown-size value.

Rationale: V1 adapters and some serializers cannot cheaply know the serialized size. Forcing inaccurate guesses will cause poor buffer sizing and fragile frame accounting.

### 5. Manifest Is A V2 API

Manifest production should be a direct V2 API, not repeated `is SerializerWithStringManifest` checks.

New native V2 serializers must return a non-empty serializer-owned manifest token. Manifests must not be CLR type names or assembly-qualified type names. This is required so V2 does not depend on polymorphic deserialization or CLR type guessing. Ports of existing serializer IDs may preserve their legacy manifest behavior, including empty or missing manifests, when changing the manifest would break existing wire or persisted data. `SerializerV1Adapter` may also preserve an empty or missing manifest when adapting legacy V1 serializers that historically omitted manifests.

Rationale: remoting and persistence both need serializer ID + manifest. V2 should make this uniform for V1 adapters, V2 hand-written serializers, and generated serializers.

### 6. Serialization.cs Stores V2 Internally

`Serialization.cs` stores V2 serializers in its ID and type maps. V1 serializers instantiated through HOCON or setup are wrapped on registration.

Public lookup APIs keep returning `Serializer` for compatibility and unwrap `SerializerV1Adapter` when necessary. Internal lookup APIs expose `SerializerV2` for buffer-first paths.

Rationale: V2 is the new cheese for Akka.NET 1.6 internally, but public compatibility is required while classic remoting, persistence, and user serializers still rely on V1 APIs.

### 7. Classic Remoting Is Compatibility, Not Zero-Copy

Classic remoting remains a byte-array compatibility path and preserves its existing protobuf wire format. Native V2 serializers can still participate through inherited bridge methods, but classic remoting does not need direct `IBufferWriter<byte>` integration.

Rationale: the purpose of the classic bridge is to keep existing classic remoting behavior working. Classic remoting will still allocate at protobuf / `ByteString` boundaries. The zero-copy remoting path belongs to Artery.

### 8. Akka.Delivery Is The Initial V2 Buffer POC

Akka.Delivery chunking should use internal V2 serialization APIs to write payloads through `IBufferWriter<byte>` and deserialize assembled chunks through `ReadOnlySequence<byte>`.

Rationale: delivery already owns chunked byte payloads and is not tied to classic remoting protobuf envelopes. It is the lowest-risk production path for proving native V2 buffer APIs before Artery.

### 9. Persistence Compatibility Is Part Of The Foundation

Persistence event and snapshot serializers must use V2 and preserve stored data compatibility in this same change.

Rationale: persistence is the highest-risk compatibility surface. Old journal and snapshot data must remain readable, and V2 payloads must store serializer ID + manifest + bytes in the same conceptual model.

### 10. Sourcegen Comes Next

Source-generated MessagePack serializers should be implemented only after this foundation is green.

Rationale: sourcegen validates the V2 API through real serialization, classic remoting, and persistence paths before Artery envelopes depend on it.

### 11. System UID Is 64-Bit In Any V2 Schema

Any V2 or sourcegen schema that carries the address/system UID (`UniqueAddress`, quarantine, heartbeat, handshake, Artery envelope origin) MUST emit and read it as a 64-bit integer (`long`). No V2 schema may introduce a 32-bit uid field.

Rationale: `widen-system-uid-to-64bit` (Milestone 3.5) re-types the system UID to `long` end-to-end as a prerequisite for Artery, whose frame header carries a 64-bit origin UID. This change does not block on serializer-v2, but constrains its schema (see that change's design.md, Decision 4).

## Risks / Trade-offs

**Compatibility inheritance tension**: `SerializerV2` being usable as `Serializer` keeps this PR compatible, but permits awkward compositions such as wrapping V2 with `SerializerV1Adapter`. Guardrails should be added before native V2 serializers become common.

**API break blast radius**: internal V2 APIs should be used for new code, but public lookup APIs remain compatible for this foundation PR.

**Persistence compatibility**: old data must remain readable. Add explicit tests with V1-serialized event and snapshot bytes.

**Classic remoting allocation remains**: acceptable for compatibility. Do not over-optimize classic remoting while Artery is the target high-throughput path.

**Async API uncertainty**: persistence may need async serializer behavior. Decide before Artery, even if the initial API remains sync.

**V1 adapter behavior**: adapter must preserve identifiers, manifests, error semantics, and transport information handling.
