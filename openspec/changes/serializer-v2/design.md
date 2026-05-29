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
- Intentionally break `FindSerializerFor()` / `FindSerializerForType()` return types to return `SerializerV2`.
- Keep V1 serializers working through `SerializerV1Adapter`.
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

### 1. SerializerV2 Is Canonical And Independent

`SerializerV2` is a new base class that does not inherit from `Serializer` or `SerializerWithStringManifest`.

V1 compatibility is provided by `SerializerV1Adapter : SerializerV2`.

Rationale: inheriting from `Serializer` permanently couples V2 to the `byte[]` API. The new API needs buffer-first methods for remoting, persistence, and source-generated serializers. Compatibility belongs in an adapter.

### 2. V2 Still Provides Bridge Methods

`SerializerV2` should expose compatibility bridge methods such as `ToBinary` and `FromBinary` so existing code paths can be migrated incrementally.

These bridge methods should be implemented in terms of the V2 buffer API for native V2 serializers and delegated to the inner serializer for V1 adapters.

Rationale: classic remoting and persistence still need byte arrays at protobuf boundaries. Bridge methods keep those compatibility paths clear without making `Serializer` the base abstraction.

### 3. Serialize Must Report Bytes Written

The V2 serialize API must make frame and payload length accounting explicit.

The exact shape can be `int`, `ValueTask<int>`, or a small result type, but callers must not have to infer bytes written from unrelated state when building envelopes.

Rationale: Artery envelopes and frame encoders need accurate payload length accounting. Classic remoting and persistence can ignore this in bridge paths, but the API must be suitable before sourcegen and Artery work begins.

### 4. SizeHint Needs Unknown Size

`SizeHint` must support an unknown-size value.

Rationale: V1 adapters and some serializers cannot cheaply know the serialized size. Forcing inaccurate guesses will cause poor buffer sizing and fragile frame accounting.

### 5. Manifest Is A V2 API

Manifest production should be a direct V2 API, not repeated `is SerializerWithStringManifest` checks.

Rationale: remoting and persistence both need serializer ID + manifest. V2 should make this uniform for V1 adapters, V2 hand-written serializers, and generated serializers.

### 6. Serialization.cs Stores V2 Internally

`Serialization.cs` stores V2 serializers in its ID and type maps. V1 serializers instantiated through HOCON or setup are wrapped on registration.

Rationale: V2 is the new cheese for Akka.NET 1.6. The API break is intentional and should be visible instead of hidden behind overloads that keep old assumptions alive.

### 7. Classic Remoting Is Compatibility, Not Zero-Copy

Classic remoting should use V2 payload serialization but preserve its existing protobuf wire format.

Rationale: the purpose of the classic bridge is to keep existing classic remoting behavior working after the V2 API break. Classic remoting will still allocate at protobuf / `ByteString` boundaries. The zero-copy remoting path belongs to Artery.

### 8. Persistence Compatibility Is Part Of The Foundation

Persistence event and snapshot serializers must use V2 and preserve stored data compatibility in this same change.

Rationale: persistence is the highest-risk compatibility surface. Old journal and snapshot data must remain readable, and V2 payloads must store serializer ID + manifest + bytes in the same conceptual model.

### 9. Sourcegen Comes Next

Source-generated MessagePack serializers should be implemented only after this foundation is green.

Rationale: sourcegen validates the V2 API through real serialization, classic remoting, and persistence paths before Artery envelopes depend on it.

## Risks / Trade-offs

**API break blast radius**: changing return types from `Serializer` to `SerializerV2` will produce many compile errors. This is expected and should be used as the task list.

**Persistence compatibility**: old data must remain readable. Add explicit tests with V1-serialized event and snapshot bytes.

**Classic remoting allocation remains**: acceptable for compatibility. Do not over-optimize classic remoting while Artery is the target high-throughput path.

**Async API uncertainty**: persistence may need async serializer behavior. Decide before Artery, even if the initial API remains sync.

**V1 adapter behavior**: adapter must preserve identifiers, manifests, error semantics, and transport information handling.
