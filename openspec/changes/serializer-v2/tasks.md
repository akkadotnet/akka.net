## 1. SerializerV2 Base Class (Core Akka)

- [ ] 1.1 Create `SerializerV2` abstract class in `src/core/Akka/Serialization/SerializerV2.cs` with `Serialize(IBufferWriter<byte>)`, `Deserialize(ReadOnlySequence<byte>)`, `Manifest()`, `Identifier`, `SizeHint()`, `ToBinary()` bridge, `FromBinary()` bridge
- [ ] 1.2 Create `SerializerV1Adapter : SerializerV2` in `src/core/Akka/Serialization/SerializerV1Adapter.cs` — wraps V1 `Serializer`/`SerializerWithStringManifest`, exposes `Inner` property
- [ ] 1.3 Modify `Serialization.cs`: change `_serializersById` to `Dictionary<int, SerializerV2>`, `_serializerMap` to `ConcurrentDictionary<Type, SerializerV2>`
- [ ] 1.4 Modify `Serialization.cs` constructor: auto-wrap V1 serializers from HOCON in `SerializerV1Adapter`, detect and store V2 serializers directly
- [ ] 1.5 Update `FindSerializerFor()` return type to `SerializerV2`
- [ ] 1.6 Update `Deserialize(bytes, serializerId, manifest)` to use V2 dispatch
- [ ] 1.7 Fix all callers of `FindSerializerFor()` across the codebase for the return type change
- [ ] 1.8 Modify `MessageSerializer.cs`: call `serializer.Manifest(message)` directly, remove `is SerializerWithStringManifest` type check
- [ ] 1.9 Unit tests: `SerializerV1Adapter` round-trip with built-in serializers
- [ ] 1.10 Verify all existing serialization tests pass (V1 auto-wrapped transparently)

## 2. Akka.Serialization.V2 Package

- [ ] 2.1 Create `src/core/Akka.Serialization.V2/` project, target `netstandard2.1;net6.0`, reference `Akka` + `MessagePack`
- [ ] 2.2 Port `AkkaWriter` (sealed class) from POC PR #42: typed write methods, `BeginObject`, `RawBuffer` escape hatch
- [ ] 2.3 Port `AkkaReader` (sealed class) from POC PR #42: typed read methods, `BeginReadObject`, `SkipField`, `TryReadNull`, consumed offset tracking
- [ ] 2.4 Create `MessagePackSerializer : SerializerV2` base class: bridges `Serialize` → `Write(AkkaWriter)`, `Deserialize` → `Read(AkkaReader)`
- [ ] 2.5 Create `MessagePackSerializer<TProtocol>` generic variant for protocol scoping
- [ ] 2.6 Port attributes from POC: `[AkkaSerializable]`, `[AkkaField(index)]`, `[AkkaSerializer]`
- [ ] 2.7 Add `MessagePack` version to `Directory.Build.props`
- [ ] 2.8 Add project to `Akka.slnx`
- [ ] 2.9 Unit tests: `AkkaWriter`/`AkkaReader` round-trip for all supported types (primitives, DateTime, Guid, Decimal, collections, nullable, nested objects)

## 3. Hand-Written Validation Serializers

- [ ] 3.1 Create a hand-written `MessagePackSerializer` for a sample user message type (demonstrate the Write/Read API without codegen)
- [ ] 3.2 Test: round-trip serialization through full `Serialization.cs` pipeline
- [ ] 3.3 Test: register V2 serializer via HOCON, send message to remote actor
- [ ] 3.4 Test: V1 and V2 serializers coexist (different message types, different serializer IDs)
- [ ] 3.5 Test: persistence write with V2 serializer, read back (verify journal stores serializerId + manifest correctly)

## 4. Internal Protobuf Serializer Migration (Simple)

- [ ] 4.1 Port `PrimitiveSerializers` (ID 17) to extend `SerializerV2` — use `proto.WriteTo(IBufferWriter<byte>)` / `Parser.ParseFrom(ReadOnlySequence<byte>)`
- [ ] 4.2 Port `ByteArraySerializer` (ID 4) to extend `SerializerV2`
- [ ] 4.3 Port `SystemMessageSerializer` (ID 22) to extend `SerializerV2`
- [ ] 4.4 Port `ClusterMessageSerializer` (ID 5) to extend `SerializerV2`
- [ ] 4.5 Verify wire format is byte-identical for each ported serializer (round-trip test with V1-serialized bytes)
- [ ] 4.6 Run full test suite: `dotnet test -c Release`

## 5. Validation

- [ ] 5.1 Verify `dotnet build -warnaserror` passes
- [ ] 5.2 Verify all Akka.Remote tests pass (V1 serializers auto-wrapped)
- [ ] 5.3 Verify all Akka.Persistence tests pass (V1 data readable)
- [ ] 5.4 Run `dotnet test -c Release src/core/Akka.API.Tests` — update API approval baselines for `FindSerializerFor` return type change
