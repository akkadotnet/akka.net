## Context

Akka.NET's serialization infrastructure centers on the `Serializer` base class (`src/core/Akka/Serialization/Serializer.cs`) with `ToBinary(object) → byte[]` and `FromBinary(byte[], Type) → object`. `SerializerWithStringManifest` extends it with manifest-based dispatch. The `Serialization` class (`src/core/Akka/Serialization/Serialization.cs`) manages registration via HOCON and `SerializationSetup`, storing serializers in `Dictionary<int, Serializer>` by ID and `ConcurrentDictionary<Type, Serializer>` by type.

All 15+ internal serializers (Cluster, Remote, Persistence, Sharding, etc.) use Google.Protobuf. They extend `SerializerWithStringManifest`, call `.ToByteArray()` for encoding, and `Parser.ParseFrom(byte[])` for decoding. Four of them (Sharding, PubSub, ReliableDelivery, Misc) wrap arbitrary user payloads via `WrappedPayloadSupport`, which calls `FindSerializerFor()` → `ToBinary()` on the inner message.

A POC at github.com/Aaronontheweb/AkkaSerializationPoC (PR #42, spike/serializer-v2-redesign branch) validated the approach: sealed `AkkaWriter`/`AkkaReader` classes wrapping MessagePack, 22% faster deserialization than the interface-based dev branch, -322 net lines. The source generator is deferred — hand-written serializers validate the API first.

## Goals / Non-Goals

**Goals:**
- `SerializerV2` base class in core Akka with `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API
- `SerializerV1Adapter` wraps legacy serializers to V2
- `Serialization.cs` uses V2 internally (auto-wraps V1 on registration)
- `MessagePackSerializer : SerializerV2` + sealed `AkkaWriter`/`AkkaReader` in separate package
- Mechanical port of simple internal Protobuf serializers to V2 base (same IDs, same wire format)
- Hand-written serializers validate the API before source generator investment
- Persistence data fully backward compatible (V1-serialized events remain readable)

**Non-Goals:**
- Source generator (deferred until API validated)
- Rewriting internal Protobuf serializers to use MessagePack (they stay Protobuf, just change base class)
- Changing persistence envelope serializers (PersistenceMessageSerializer, PersistenceSnapshotSerializer)
- Changing the HOCON registration mechanism
- HOCON-less or attribute-only registration (future enhancement)

## Decisions

### 1. SerializerV2 is independent — does not extend Serializer

**Decision:** `SerializerV2` is a new base class with no inheritance relationship to `Serializer` or `SerializerWithStringManifest`. V1 serializers are wrapped in `SerializerV1Adapter : SerializerV2`.

**Rationale:** Having V2 extend V1 permanently couples the new system to the `byte[]`-based API. The bridge methods (`ToBinary`/`FromBinary`) exist on V2 for transport compatibility but are implemented in terms of the buffer API (not inherited from V1). This allows the transport to eventually bypass the bridge entirely.

### 2. Two-layer design: core base class + MessagePack package

**Decision:**
- Layer 1: `SerializerV2` in core Akka — codec-agnostic, no MessagePack dependency
- Layer 2: `MessagePackSerializer : SerializerV2` in `Akka.Serialization.V2` — bridges to `AkkaWriter`/`AkkaReader`

```
SerializerV2 (core Akka — IBufferWriter/ReadOnlySequence, no MessagePack)
  ├── SerializerV1Adapter (wraps legacy Serializer)
  ├── MessagePackSerializer (Akka.Serialization.V2 — AkkaWriter/AkkaReader)
  │     └── MessagePackSerializer<TProtocol> (protocol-scoped for future codegen)
  └── [Internal Protobuf serializers extend SerializerV2 directly]
```

**Rationale:** Core Akka should not depend on MessagePack. Internal Protobuf serializers extend `SerializerV2` directly and use `proto.WriteTo(IBufferWriter<byte>)` — they don't need the MessagePack layer. User-facing serializers use `MessagePackSerializer` via the separate package.

### 3. Serialization.cs stores SerializerV2 internally

**Decision:** Change `_serializersById` to `Dictionary<int, SerializerV2>` and `_serializerMap` to `ConcurrentDictionary<Type, SerializerV2>`. V1 serializers instantiated from HOCON are auto-wrapped in `SerializerV1Adapter`. `FindSerializerFor()` returns `SerializerV2`.

**Rationale:** V2 is the new foundation. All dispatch goes through V2. `SerializerV1Adapter.Inner` provides access to the underlying V1 serializer for callers that need backward compat.

### 4. Mechanical port of internal Protobuf serializers

**Decision:** Simple internal serializers (`ClusterMessageSerializer`, `SystemMessageSerializer`, `PrimitiveSerializers`, `ByteArraySerializer`) change base class to `SerializerV2` and use `proto.WriteTo(IBufferWriter<byte>)` / `Parser.ParseFrom(ReadOnlySequence<byte>)`. Same serializer IDs, same manifests, same wire format. Google.Protobuf natively supports both APIs.

**Rationale:** It's a find-and-replace level change. Wire format is byte-identical. No new IDs needed. Serializers with nested user payloads via `WrappedPayloadSupport` (Sharding, PubSub, ReliableDelivery, Misc) are deferred until the API is validated with simpler serializers first.

### 5. AkkaWriter/AkkaReader are sealed (from POC PR #42)

**Decision:** Use the spike branch approach — sealed concrete classes wrapping MessagePack directly, no `ICodecWriter`/`ICodecReader` interface abstraction.

**Rationale:** 22% faster deserialization from JIT devirtualization. The interface layer was YAGNI — we're committed to MessagePack for user message codegen. `AkkaWriter.RawBuffer` escape hatch preserves extensibility for advanced scenarios.

### 6. Bridge methods for transport compatibility

**Decision:** `SerializerV2` has `ToBinary(object) → byte[]` and `FromBinary(byte[], string) → object` implemented in terms of the buffer API:

```csharp
public virtual byte[] ToBinary(object obj)
{
    var buffer = new ArrayBufferWriter<byte>(SizeHint(obj));
    Serialize(buffer, obj);
    return buffer.WrittenSpan.ToArray();
}

public virtual object FromBinary(byte[] bytes, string manifest)
    => Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
```

**Rationale:** The current transport (and Spec 3's initial `FrameBufferWriter` path) can call `Serialize(IBufferWriter<byte>)` directly. The bridge is for backward compat with code that still expects `byte[]`. The bridge is virtual so it can be bypassed when direct buffer access is available.

## Risks / Trade-offs

**[FindSerializerFor return type change]** → Breaking API change. All callers that type-check the result against `Serializer` or `SerializerWithStringManifest` must update. Mitigated: `SerializerV1Adapter.Inner` provides access to the original V1 serializer.

**[MessagePack as a new dependency]** → Only in the `Akka.Serialization.V2` package, not core Akka. Users who don't opt in are unaffected. MessagePack-CSharp has 215M+ downloads and is widely used in production.

**[Internal serializer port could introduce bugs]** → Mitigated by wire format being byte-identical (same Protobuf bytes, same IDs). Existing serialization round-trip tests catch regressions.

**[WrappedPayloadSupport serializers deferred]** → The 4 serializers with nested user payloads continue using V1 `FindSerializerFor()` → `ToBinary()` through the adapter. They work correctly but miss the zero-copy benefit for the inner payload. Addressed in a follow-up pass after API validation.
