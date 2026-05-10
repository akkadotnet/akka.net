## Context

Akka.NET's serialization infrastructure centers on the `Serializer` base class (`src/core/Akka/Serialization/Serializer.cs`) with `ToBinary(object) → byte[]` and `FromBinary(byte[], Type) → object`. `SerializerWithStringManifest` extends it with manifest-based dispatch. The `Serialization` class (`src/core/Akka/Serialization/Serialization.cs`) manages registration via HOCON and `SerializationSetup`, storing serializers in `Dictionary<int, Serializer>` by ID and `ConcurrentDictionary<Type, Serializer>` by type.

All 15+ internal serializers (Cluster, Remote, Persistence, Sharding, etc.) use Google.Protobuf, extend `SerializerWithStringManifest`, call `.ToByteArray()` for encoding, and `Parser.ParseFrom(byte[])` for decoding. Four of them (Sharding, PubSub, ReliableDelivery, Misc) wrap arbitrary user payloads via `WrappedPayloadSupport`, which calls `FindSerializerFor()` → `ToBinary()` on the inner message.

A POC at github.com/Aaronontheweb/AkkaSerializationPoC (PR #42, spike/serializer-v2-redesign branch) explored a sealed `AkkaWriter`/`AkkaReader` design over MessagePack and measured a 22% deserialization win against the interface-based alternative. **That work is deferred to a separate change (`serializer-v2-codegen`)** — it's the user-facing codec story and ships as a unit with the Roslyn source generator. This change establishes the foundation only.

## Goals / Non-Goals

**Goals:**

- `SerializerV2` base class in core Akka with `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API
- `SerializerV1Adapter` wraps legacy serializers to V2
- `Serialization.cs` uses V2 internally (auto-wraps V1 on registration)
- `ByteArraySerializer` and `PrimitiveSerializers` ported to V2 base — same IDs, byte-identical wire format
- A standalone benchmark that simulates `EndpointWriter`'s serialize-frame-deserialize chain on the V2 API and quantifies V2-direct vs `byte[]`-bridge cost
- Persistence data fully backward compatible (V1-serialized events remain readable)

**Non-Goals:**

- MessagePack codec, `AkkaWriter`/`AkkaReader`, attributes, `Akka.Serialization.V2` package — deferred to `serializer-v2-codegen` along with the source generator
- Mechanical port of `ClusterMessageSerializer`, `SystemMessageSerializer`, and other Protobuf-based serializers — trivial work but no urgency until V2 API is locked
- Changing persistence envelope serializers (`PersistenceMessageSerializer`, `PersistenceSnapshotSerializer`)
- Changing the HOCON registration mechanism
- HOCON-less or attribute-only registration (future enhancement)

## Decisions

### 1. SerializerV2 is independent — does not extend Serializer

**Decision:** `SerializerV2` is a new base class with no inheritance relationship to `Serializer` or `SerializerWithStringManifest`. V1 serializers are wrapped in `SerializerV1Adapter : SerializerV2`.

**Rationale:** Having V2 extend V1 permanently couples the new system to the `byte[]`-based API. The bridge methods (`ToBinary` / `FromBinary`) exist on V2 for transport compatibility but are implemented in terms of the buffer API (not inherited from V1). This allows the transport to eventually bypass the bridge entirely (in Spec 3, `EndpointWriter` will call `Serialize(IBufferWriter)` directly).

### 2. Single layer in core Akka — no new package

**Decision:** `SerializerV2`, `SerializerV1Adapter`, and the V2-ported primitives all live in core Akka (`src/core/Akka/Serialization/` and `src/core/Akka.Remote/Serialization/`). No new NuGet package is created in this change.

**Rationale:** A separate `Akka.Serialization.V2` package only earns its keep when paired with the user-facing codec story (MessagePack runtime, `AkkaWriter`/`AkkaReader`, attributes) and the source generator that makes it ergonomic. Shipping an empty-shell package now, then redesigning its public surface once codegen lands, would be churn. Both ship together when the codegen change lands.

### 3. Serialization.cs stores SerializerV2 internally

**Decision:** Change `_serializersById` to `Dictionary<int, SerializerV2>` and `_serializerMap` to `ConcurrentDictionary<Type, SerializerV2>`. V1 serializers instantiated from HOCON are auto-wrapped in `SerializerV1Adapter`. `FindSerializerFor()` returns `SerializerV2`.

**Rationale:** V2 is the new foundation. All dispatch goes through V2. `SerializerV1Adapter.Inner` provides access to the underlying V1 serializer for callers that need backward compat. Detection on registration is `if (instance is SerializerV2 v2) store(v2); else store(new SerializerV1Adapter(v1));`.

### 4. Reference implementation: ByteArraySerializer + PrimitiveSerializers

**Decision:** Port `ByteArraySerializer` (ID 4) and `PrimitiveSerializers` (ID 17) to extend `SerializerV2`. Both are hand-rolled (`Encoding.UTF8`, `BitConverter`, identity passthrough). Same serializer IDs. Byte-identical wire format. Other internal serializers (`ClusterMessageSerializer`, `SystemMessageSerializer`, etc.) keep V1 + auto-wrap in this change.

**Rationale:** These are the smallest possible reference for the V2 API. They exercise the full `IBufferWriter<byte>` / `ReadOnlySequence<byte>` round trip without dragging in `Google.Protobuf`'s `WriteTo(IBufferWriter)` integration as a confounder. If V2-direct doesn't beat V1-bridge on UTF-8 strings, fixed-width integers, and a byte[] passthrough, the API has fundamental allocation problems we need to fix before doing anything else.

The Protobuf serializer ports are deferred not because they're hard, but because they're work without a near-term consumer — `EndpointWriter` doesn't switch to direct buffer access until Spec 3, and `WrappedPayloadSupport` serializers are deferred regardless. The mechanical port can ride along with `serializer-v2-codegen` or sit as a tiny standalone follow-on once a downstream consumer needs it.

### 5. Bridge methods for transport compatibility

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

**Rationale:** Akka.Remote's current `EndpointWriter` and persistence journals still operate on `byte[]`. The bridge keeps them working unchanged. The bridge is virtual so that `SerializerV1Adapter` can override it to delegate directly to the wrapped V1 serializer's `ToBinary` / `FromBinary` (avoiding a pointless `Serialize-into-buffer-then-ToArray` round trip when the underlying serializer is `byte[]`-native anyway).

### 6. Benchmark validates the API before downstream specs build on it

**Decision:** Add a standalone benchmark to `src/benchmark/` (next to `FramingBenchmarks`) that:

1. Uses the V2-ported `PrimitiveSerializers` and `ByteArraySerializer` (no Akka.Remote dependency).
2. Builds a synthetic envelope shaped like what `EndpointWriter` produces — `[serializerId: int][manifest: string][payload: bytes]`.
3. Round-trips it through `Serialize(IBufferWriter)` → wrap result as `ReadOnlySequence` → `Deserialize` (V2-direct path) and through `ToBinary` → `FromBinary` (V1-bridge baseline).
4. Reports allocations, throughput, and per-op latency for representative payload sizes (small/medium/large, mixed manifest).

**Rationale:** Milestone 2 in isolation only validates the API surface — the actual end-to-end zero-copy chain isn't lit up until Spec 3 wires `EndpointWriter` to `Serialize(FrameBufferWriter)` directly. Without this benchmark, friction (if any) in the V2 API doesn't surface until Spec 3 has already built on it. The harness is also reusable: Spec 3 can drop in a real `FrameBufferWriter` and re-run to confirm no regression at integration time. The same pattern extends naturally to a follow-up that measures `WrappedPayloadSupport`'s nested-payload allocation cost.

## Risks / Trade-offs

**[FindSerializerFor return type change]** → Breaking API change. All callers that type-check the result against `Serializer` or `SerializerWithStringManifest` must update. Mitigated: `SerializerV1Adapter.Inner` provides access to the original V1 serializer.

**[V1Adapter is allocation-equivalent to V1, not better]** → Wrapping a V1 serializer in `SerializerV1Adapter` and calling `Serialize(IBufferWriter, obj)` ends up calling `inner.ToBinary(obj)` and copying into the buffer. No allocation win for V1-native serializers. Acceptable: the goal is API parity for the wrapped path, not performance. V2-native serializers get the win; V1 serializers can be migrated incrementally over future changes.

**[No MessagePack codec means no headline perf win in this change]** → The PoC's 22% deserialize win was MessagePack vs an interface-based design — irrelevant to this change since neither side ships. The V2-direct vs V1-bridge benchmark on `PrimitiveSerializers` will show a smaller, allocation-driven win (or it won't, and we know we have a problem to fix). Either way, the result is a load-bearing data point for `serializer-v2-codegen`.

**[Protobuf serializer ports deferred]** → `ClusterMessageSerializer`, `SystemMessageSerializer`, and the `WrappedPayloadSupport` serializers continue running through `SerializerV1Adapter`. Wire format unchanged, behavior unchanged. They miss the zero-copy benefit until ported, but no Akka.Remote feature regresses.

**[No user-facing codec]** → Users who want to define V2 serializers in this change have to subclass `SerializerV2` directly and write `Serialize(IBufferWriter)` / `Deserialize(ReadOnlySequence)` by hand. Not an ergonomic story — and not intended to be. The user-facing story (`MessagePackSerializer<TProtocol>` + attributes + codegen) is the explicit subject of `serializer-v2-codegen`. End users on this change keep using V1 serializers via the auto-wrap.
