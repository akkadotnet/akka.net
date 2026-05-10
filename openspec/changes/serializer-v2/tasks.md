## 1. SerializerV2 Base Class (Core Akka)

- [ ] 1.1 Create `SerializerV2` abstract class in `src/core/Akka/Serialization/SerializerV2.cs` with `Serialize(IBufferWriter<byte>, object)`, `Deserialize(ReadOnlySequence<byte>, string) → object`, `Manifest(object) → string`, `Identifier`, `SizeHint(object) → int`, virtual `ToBinary(object) → byte[]` bridge, virtual `FromBinary(byte[], string) → object` bridge
- [ ] 1.2 Create `SerializerV1Adapter : SerializerV2` in `src/core/Akka/Serialization/SerializerV1Adapter.cs` — wraps V1 `Serializer` / `SerializerWithStringManifest`, exposes `Inner` property, overrides `ToBinary` / `FromBinary` to call inner V1 directly (avoid pointless `Serialize`-then-`ToArray` round trip)
- [ ] 1.3 Modify `Serialization.cs`: change `_serializersById` to `Dictionary<int, SerializerV2>`, `_serializerMap` to `ConcurrentDictionary<Type, SerializerV2>`
- [ ] 1.4 Modify `Serialization.cs` constructor: detect `is SerializerV2` and store directly; otherwise wrap V1 instance in `SerializerV1Adapter`
- [ ] 1.5 Update `FindSerializerFor()` return type to `SerializerV2`
- [ ] 1.6 Update `Deserialize(byte[], int, string)` and any other dispatch entry points to go through V2
- [ ] 1.7 Fix all callers of `FindSerializerFor()` across the codebase for the return type change (use `Inner` where they previously did type checks)
- [ ] 1.8 Modify `MessageSerializer.cs` (Akka.Remote): call `serializer.Manifest(message)` directly, remove `is SerializerWithStringManifest` type check

## 2. SerializerV1Adapter Validation

- [ ] 2.1 Unit test: `SerializerV1Adapter` round-trips a V1 `Serializer` (use `NewtonSoftJsonSerializer` or `NullSerializer` as fixture) — `Serialize` → `Deserialize` returns equal object
- [ ] 2.2 Unit test: `SerializerV1Adapter` round-trips a V1 `SerializerWithStringManifest` — preserves manifest end-to-end
- [ ] 2.3 Unit test: `SerializerV1Adapter.Identifier` returns the wrapped V1 serializer's `Identifier`
- [ ] 2.4 Unit test: `SerializerV1Adapter.Inner` returns the original V1 instance unchanged
- [ ] 2.5 Unit test: V1 serializer registered via HOCON is auto-wrapped — `FindSerializerFor()` returns a `SerializerV1Adapter` whose `Inner` is the configured V1 instance
- [ ] 2.6 Unit test: V2 serializer registered via HOCON is stored directly (not double-wrapped)
- [ ] 2.7 Verify every existing serialization test in `Akka.Tests`, `Akka.Remote.Tests`, and `Akka.Persistence.Tests` passes unchanged (V1 transparently auto-wrapped — nothing user-observable)

## 3. V2-Native Reference Serializers

### 3.1 ByteArraySerializer

- [ ] 3.1.1 Port `src/core/Akka/Serialization/ByteArraySerializer.cs` to extend `SerializerV2` — same Identifier (4), `IncludeManifest = false`, identity passthrough behavior
- [ ] 3.1.2 `Serialize(IBufferWriter<byte>, byte[])`: copy bytes into the writer (no allocation beyond the writer's own bookkeeping)
- [ ] 3.1.3 `Deserialize(ReadOnlySequence<byte>, manifest)`: return `seq.ToArray()` (V1 contract is that callers may hold the returned array — aliasing is not allowed in this change)
- [ ] 3.1.4 Round-trip test against V1 `Serializer` baseline — produced bytes byte-identical, deserialized object equal
- [ ] 3.1.5 Wire-format test: V1-serialized bytes deserialize correctly through V2; V2-serialized bytes deserialize correctly through V1 (interop)

### 3.2 PrimitiveSerializers (string, int32, int64)

- [ ] 3.2.1 Port `src/core/Akka.Remote/Serialization/PrimitiveSerializers.cs` to extend `SerializerV2` — same Identifier (17), preserve all six manifest aliases (`S`/`I`/`L` plus the .NET Core and .NET Framework long-form variants), preserve `use-legacy-behavior` config
- [ ] 3.2.2 `Serialize(IBufferWriter<byte>, string)` via `Encoding.UTF8.GetBytes(string, IBufferWriter<byte>)` (single allocation-free encode into the writer's span)
- [ ] 3.2.3 `Serialize(IBufferWriter<byte>, int)` via `BinaryPrimitives.WriteInt32LittleEndian` into a 4-byte span obtained from `writer.GetSpan(4)`
- [ ] 3.2.4 `Serialize(IBufferWriter<byte>, long)` via `BinaryPrimitives.WriteInt64LittleEndian` into an 8-byte span
- [ ] 3.2.5 `Deserialize(ReadOnlySequence<byte>, manifest)`: dispatch by manifest. For string, use `Encoding.UTF8.GetString(ReadOnlySequence<byte>)` (multi-segment-safe). For int32/int64, use `SequenceReader<byte>` and `BinaryPrimitives.ReadInt32LittleEndian` / `ReadInt64LittleEndian`
- [ ] 3.2.6 Wire-format tests for each type:
  - [ ] string: V1-serialized bytes (UTF-8) round-trip through V2; V2 output matches `Encoding.UTF8.GetBytes` exactly
  - [ ] int32: V1 `BitConverter.GetBytes(int)` output matches V2 output; round-trips through both directions
  - [ ] int64: V1 `BitConverter.GetBytes(long)` output matches V2 output; round-trips through both directions
- [ ] 3.2.7 Manifest dispatch test: each of the six manifest aliases deserializes correctly
- [ ] 3.2.8 Multi-segment input test: synthesize a multi-segment `ReadOnlySequence<byte>` (split a UTF-8 string mid-codepoint, split an int across segments) and confirm `Deserialize` handles it correctly via `SequenceReader<byte>`

## 4. Transport-Envelope Benchmark

- [ ] 4.1 New benchmark project (or add to `src/benchmark/Akka.Benchmarks/`) — a `SerializerV2Benchmarks` class
- [ ] 4.2 Synthetic envelope writer: writes `[serializerId: int32 LE][manifestLen: int32 LE][manifest: utf8 bytes][payloadLen: int32 LE][payload: bytes]` to an `IBufferWriter<byte>`. This is a stand-in for what Spec 3's `FrameBufferWriter` / `EndpointWriter` will do.
- [ ] 4.3 Synthetic envelope reader: parses the same shape from a `ReadOnlySequence<byte>` and dispatches to `serialization.FindSerializerFor(typeOrId).Deserialize(payloadSlice, manifest)`
- [ ] 4.4 V2-direct path benchmark: serialize → wrap as `ReadOnlySequence` → deserialize using `Serialize(IBufferWriter)` / `Deserialize(ReadOnlySequence)`
- [ ] 4.5 V1-bridge baseline benchmark: same envelope, but using `ToBinary()` and `FromBinary()` (the bridge methods on `SerializerV2`)
- [ ] 4.6 Payload matrix:
  - [ ] string payloads: short ("hello"), medium (~256 chars), long (~4 KB)
  - [ ] int32 payload
  - [ ] int64 payload
  - [ ] byte[] payloads: small (16 B), medium (1 KB), large (16 KB)
- [ ] 4.7 Capture allocations and throughput; document the V2-direct vs V1-bridge delta in the change archive notes

## 5. Validation

- [ ] 5.1 `dotnet build -warnaserror` passes
- [ ] 5.2 All Akka.Remote tests pass — V1 serializers auto-wrapped transparently
- [ ] 5.3 All Akka.Persistence tests pass — V1 persisted data still readable, V2 persisted data round-trips
- [ ] 5.4 `dotnet test -c Release src/core/Akka.API.Tests` — refresh API approval baselines for `FindSerializerFor` return type change and the new `SerializerV2` / `SerializerV1Adapter` public surface
- [ ] 5.5 Run the `SerializerV2Benchmarks` and capture results in the archive notes

## 6. Out of Scope (Documented Follow-On)

These are not part of this change. Captured here as a punch list for a future change once the V2 API is validated by Sections 1–4:

- Port simple Protobuf serializers (`ClusterMessageSerializer`, `SystemMessageSerializer`) to `SerializerV2` using `proto.WriteTo(IBufferWriter<byte>)` / `Parser.ParseFrom(ReadOnlySequence<byte>)` — mechanical, byte-identical wire format, expand benchmark coverage to include them
- Address `WrappedPayloadSupport` serializers (Sharding, PubSub, ReliableDelivery, Misc) — currently allocate `byte[]` per nested payload via the V1 path
- MessagePack codec, `AkkaWriter` / `AkkaReader`, `[AkkaSerializable]` / `[AkkaField]` / `[AkkaSerializer]` attributes, `Akka.Serialization.V2` package, Roslyn source generator — all bundled into the `serializer-v2-codegen` change
