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

### 12. Pooled Writer + Ownership Contract

`Akka.Serialization.PooledPayloadWriter` is a public, sealed `IBufferWriter<byte>` on the core V2 surface: a growable buffer rented from an `ArrayPool<byte>` (double-and-copy growth) that adds three capabilities the bare `IBufferWriter<byte>` interface cannot express:

- **Read-back**: `WrittenCount` / `WrittenSpan` / `WrittenMemory` expose the bytes written so far.
- **Patch**: `GetPatchSpan(start, length)` returns a mutable view over already-written bytes, for the reserve-then-patch pattern (a length prefix or fixed header whose value is only known once later bytes -- literals, payload -- have been written).
- **Detach (ownership transfer)**: `Detach()` returns an `IMemoryOwner<byte>` whose `.Memory` is exactly the written slice; disposing the owner returns the array to the pool. This lets a transport complete an asynchronous socket write against the detached memory and only then release it back to the pool -- no intermediate copy. After `Detach()` the writer is spent: every member except `Dispose()` throws `ObjectDisposedException` (including a second `Detach()` call); the writer's own `Dispose()` becomes a no-op because ownership already moved. `Dispose()` without a prior `Detach()` returns the array to the pool (idempotent). `Reset()` reuses the current rented array without re-renting, valid only while the writer is still alive.

This contract was first invented privately as Artery G1's internal `PooledFrameWriter`, because `IBufferWriter<byte>` alone cannot express read-back, patch, or ownership hand-off. Artery G1/G2 friction made clear this belongs on the core V2 surface rather than being reinvented per-transport: any V2 encode path (Artery, a future sourcegen'd serializer, a user transport) needs the same reserve/patch/detach shape. Semantically it plays the role of Pekko's `EnvelopeBufferPool`: a reusable wire buffer whose lifetime is decoupled from the encode call that filled it.

**Buffer source is injectable**: the constructor accepts an optional `ArrayPool<byte>` (default `ArrayPool<byte>.Shared`); every rent and return in the writer's lifetime -- initial rent, growth, `Dispose()`, and the return performed when the `IMemoryOwner<byte>` from `Detach()` is disposed -- goes through that same pool, which covers dedicated per-transport pools AND POH-pinned arrays via a custom `ArrayPool<byte>` subclass (artery design.md, Decision 9: POH-pinned only if pinning churn shows in measurement) without inventing a bespoke buffer-source interface or switching to `MemoryPool<byte>`. `Detach()`'s `IMemoryOwner<byte>` return type deliberately leaves room for a future pooled-owner implementation (Pekko `EnvelopeBufferPool`-style) without an API change.

**`maxCapacity` and deterministic oversized-payload failure**: the constructor accepts an optional `maxCapacity` (default `int.MaxValue`). Any `GetSpan` / `GetMemory` / `Advance` call that would push the written count past `maxCapacity` throws `Akka.Serialization.PayloadSizeExceededException : AkkaException`, carrying the attempted size and the configured cap. This is deliberate groundwork for messagepack-sourcegen task 6.8 (deterministic oversized-payload failure): an oversized payload must fail HERE, at encode time, with a typed exception -- never discovered downstream as a corrupt or truncated wire frame.

`Akka.Remote.Artery.ArteryEnvelopeCodec`'s V2 single-pass `Encode` overload was refactored onto `PooledPayloadWriter` directly; the private `PooledFrameWriter` it previously had to invent for itself is deleted.

Rationale: SerializerV2 is unshipped, so this refactor is free. Landing the pooled-writer contract in core now -- rather than after Artery G2 or sourcegen depend on their own private copies -- avoids the exact duplication (`PooledFrameWriter`) this decision retires, and gives sourcegen task 6.8 a typed, encode-time failure mode to build on instead of inventing its own.

### 13. V2 API Stays Synchronous For 1.6 (task 1.7 sign-off)

`SerializerV2.Serialize` / `Deserialize` remain synchronous for the entire 1.6 cycle. This is a validated decision, not a default:

- **The hot path is structurally synchronous.** Generated and hand-written MessagePack serializers drive `MessagePack.MessagePackWriter` / `MessagePackReader`, which are `ref struct` cursors — they cannot cross an `await` boundary. An async `Serialize` signature would still have to stage every byte synchronously and could only await a flush the serializer does not own.
- **The transport already owns the async boundary.** Artery's single async step is the socket write, and the `PooledPayloadWriter.Detach()` ownership contract (Decision 12) exists precisely to decouple that from the synchronous encode. Async serialization would re-solve a problem the buffer contract already solved, at the cost of `ValueTask` machinery per message.
- **The decode side is throughput-critical and serial.** Artery parses envelope headers on a serial decode island (artery-tcp-remoting design.md, Decision 2); introducing awaits there lowers the serial-island ceiling that bounds total inbound throughput.
- **V1 compatibility forbids it.** `SerializerV1Adapter` and the classic remoting/persistence bridges expose synchronous `ToBinary`/`FromBinary`. An async V2 core would force sync-over-async at every bridge, which this codebase bans as a deadlock hazard.

Persistence scenarios that genuinely want asynchrony (e.g. claim-check serializers that fetch external payloads) are a plugin-layer concern: journals and snapshot stores already run async around synchronous serialization. If a truly async serializer contract is ever needed, it arrives post-1.6 as a separate opt-in interface (e.g. `IAsyncSerializerV2`) under extend-only rules — it must not change the sync V2 contract that Artery and sourcegen build against. This closes the "Async API uncertainty" risk below.

### 14. Manifest Is A Documented Invariant: Cheap, Stable, Derivable Without Serializing

`SerializerV2.Manifest(object)` is load-bearing beyond lookup dispatch: Artery's single-pass envelope encode writes the header — including the manifest literal or compression tag — *before* the payload is serialized (artery-tcp-remoting design.md, "Envelope wire layout"), and manifest compression tables key on the manifest string. The following are therefore a documented contract for every V2 serializer, pinned by spec test:

- **Derivable without serializing**: `Manifest(object)` must not require a prior or accompanying `Serialize` call (the signature enforces this structurally — it receives no buffer).
- **Cheap**: type-dispatch cost only (no allocation beyond the returned string, which should be a constant); it runs once per message on the remoting hot path before any payload work.
- **Stable**: the same runtime type through the same serializer returns the same manifest string on every call, in every process, across versions — manifests are wire and persistence contracts (see Decision 5 for the non-empty/non-CLR rules and legacy exemptions).
- **Bounded**: the UTF-8 encoding of a manifest must fit in Artery's envelope literal encoding — hard limit 65,535 bytes (u16 length prefix); in practice manifests should be short tokens, since every uncompressed occurrence rides the wire.

## Risks / Trade-offs

**Compatibility inheritance tension**: `SerializerV2` being usable as `Serializer` keeps this PR compatible, but permits awkward compositions such as wrapping V2 with `SerializerV1Adapter`. Guardrails should be added before native V2 serializers become common.

**API break blast radius**: internal V2 APIs should be used for new code, but public lookup APIs remain compatible for this foundation PR.

**Persistence compatibility**: old data must remain readable. Add explicit tests with V1-serialized event and snapshot bytes.

**Classic remoting allocation remains**: acceptable for compatibility. Do not over-optimize classic remoting while Artery is the target high-throughput path.

**Async API uncertainty**: persistence may need async serializer behavior. Decide before Artery, even if the initial API remains sync.

**V1 adapter behavior**: adapter must preserve identifiers, manifests, error semantics, and transport information handling.
