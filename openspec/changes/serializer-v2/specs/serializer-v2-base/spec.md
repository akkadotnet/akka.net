## ADDED Requirements

### Requirement: SerializerV2 base class with buffer API

The system SHALL provide an abstract `SerializerV2` class in core Akka with `Serialize(IBufferWriter<byte>, object)` and `Deserialize(ReadOnlySequence<byte>, string)` as the primary API. It SHALL NOT extend `Serializer` or `SerializerWithStringManifest`.

#### Scenario: Serialize to IBufferWriter

- **WHEN** `SerializerV2.Serialize(buffer, obj)` is called
- **THEN** the serializer SHALL write the serialized bytes directly into the provided `IBufferWriter<byte>` without allocating an intermediate `byte[]`

#### Scenario: Deserialize from ReadOnlySequence

- **WHEN** `SerializerV2.Deserialize(buffer, manifest)` is called with a `ReadOnlySequence<byte>`
- **THEN** the serializer SHALL read from the sequence and return the deserialized object

#### Scenario: Multi-segment ReadOnlySequence input

- **WHEN** `SerializerV2.Deserialize(buffer, manifest)` is called with a `ReadOnlySequence<byte>` whose payload spans multiple segments
- **THEN** the serializer SHALL handle the segmented input correctly (typically via `SequenceReader<byte>`) and produce the same object as it would for a single-segment input of the same bytes

#### Scenario: ToBinary bridge method

- **WHEN** `SerializerV2.ToBinary(obj)` is called
- **THEN** it SHALL by default create an `ArrayBufferWriter<byte>`, call `Serialize()`, and return the written bytes as `byte[]`. Subclasses MAY override this for direct paths.

#### Scenario: FromBinary bridge method

- **WHEN** `SerializerV2.FromBinary(bytes, manifest)` is called
- **THEN** it SHALL by default wrap the `byte[]` in `new ReadOnlySequence<byte>(bytes)` and call `Deserialize()`. Subclasses MAY override this for direct paths.

### Requirement: SerializerV1Adapter wraps legacy serializers

The system SHALL provide `SerializerV1Adapter : SerializerV2` that wraps any `Serializer` or `SerializerWithStringManifest` instance to participate in the V2 infrastructure.

#### Scenario: V1 serializer wrapped for V2 dispatch

- **WHEN** a V1 `Serializer` is registered via HOCON or `SerializationSetup`
- **THEN** `Serialization.cs` SHALL auto-wrap it in `SerializerV1Adapter` for internal storage

#### Scenario: Adapter delegates to V1 ToBinary/FromBinary

- **WHEN** `SerializerV1Adapter.Serialize(buffer, obj)` is called
- **THEN** it SHALL call the inner V1 serializer's `ToBinary(obj)` and write the resulting bytes to the buffer

#### Scenario: Adapter overrides bridge methods to skip the round trip

- **WHEN** `SerializerV1Adapter.ToBinary(obj)` or `FromBinary(bytes, manifest)` is called
- **THEN** it SHALL delegate directly to the inner V1 serializer's `ToBinary` / `FromBinary` rather than going through `Serialize → ArrayBufferWriter → ToArray`, since the inner V1 serializer is already `byte[]`-native

#### Scenario: Adapter preserves serializer identity

- **WHEN** `SerializerV1Adapter.Identifier` is accessed
- **THEN** it SHALL return the inner V1 serializer's `Identifier`

#### Scenario: Adapter preserves manifest behavior

- **WHEN** the wrapped V1 serializer is a `SerializerWithStringManifest`
- **THEN** `SerializerV1Adapter.Manifest(obj)` SHALL return the inner serializer's manifest for `obj`

- **WHEN** the wrapped V1 serializer is a plain `Serializer` (no manifest support)
- **THEN** `SerializerV1Adapter.Manifest(obj)` SHALL return an empty string (or the type's qualified name, matching V1 fallback behavior in `MessageSerializer`)

#### Scenario: Access to inner V1 serializer

- **WHEN** code needs the original V1 `Serializer` instance
- **THEN** `SerializerV1Adapter.Inner` SHALL return the wrapped V1 serializer

### Requirement: Serialization.cs uses V2 internally

The `Serialization` class SHALL store `SerializerV2` instances in its internal dictionaries and return `SerializerV2` from `FindSerializerFor()`.

#### Scenario: FindSerializerFor returns SerializerV2

- **WHEN** `Serialization.FindSerializerFor(msg)` is called
- **THEN** it SHALL return a `SerializerV2` instance (either a native V2 serializer or a `SerializerV1Adapter` wrapping a V1 serializer)

#### Scenario: V1 serializers auto-wrapped on registration

- **WHEN** a V1 serializer is instantiated from HOCON configuration
- **THEN** it SHALL be wrapped in `SerializerV1Adapter` before storage in internal dictionaries

#### Scenario: V2 serializers registered directly

- **WHEN** a V2 serializer is instantiated from HOCON configuration (detected by `is SerializerV2`)
- **THEN** it SHALL be stored directly without wrapping

#### Scenario: Deserialize dispatches through V2

- **WHEN** `Serialization.Deserialize(bytes, serializerId, manifest)` is called
- **THEN** it SHALL look up the `SerializerV2` by ID and call `FromBinary(bytes, manifest)` (which the adapter overrides to a direct V1 path, and V2-native serializers implement via the buffer API)

### Requirement: MessageSerializer uses V2 dispatch

The `MessageSerializer` in Akka.Remote SHALL use V2 serializer dispatch, calling `Manifest()` directly on `SerializerV2` without type-checking for `SerializerWithStringManifest`.

#### Scenario: Serialize with manifest

- **WHEN** `MessageSerializer.Serialize(system, transportInfo, message)` is called
- **THEN** it SHALL call `serializer.Manifest(message)` directly (all V2 serializers expose `Manifest`) and include it in the wire message

### Requirement: ByteArraySerializer ported to V2

`ByteArraySerializer` SHALL extend `SerializerV2`, preserve serializer ID 4, and produce byte-identical wire format to its V1 implementation.

#### Scenario: V2 serialize copies bytes into the writer

- **WHEN** `ByteArraySerializer.Serialize(buffer, byte[])` is called
- **THEN** it SHALL write the byte array unchanged into the provided `IBufferWriter<byte>`

#### Scenario: V2 deserialize returns a fresh array

- **WHEN** `ByteArraySerializer.Deserialize(seq, manifest)` is called
- **THEN** it SHALL return a `byte[]` materialized from the sequence (callers may retain the returned reference; aliasing is not permitted in this change)

#### Scenario: Wire format byte-identical to V1

- **WHEN** the same `byte[]` payload is serialized through V1 `ByteArraySerializer.ToBinary` and through V2 `ByteArraySerializer.Serialize` followed by collecting the writer's bytes
- **THEN** the produced byte sequences SHALL be equal

### Requirement: PrimitiveSerializers ported to V2

`PrimitiveSerializers` SHALL extend `SerializerV2`, preserve serializer ID 17, support `string` / `int` / `long` payloads with all six existing manifest aliases, and produce byte-identical wire format to its V1 implementation.

#### Scenario: String serialization

- **WHEN** `PrimitiveSerializers.Serialize(buffer, string s)` is called
- **THEN** it SHALL write `Encoding.UTF8.GetBytes(s)` into the buffer (using a `IBufferWriter<byte>`-aware encoding path that avoids an intermediate `byte[]` allocation when possible)

#### Scenario: String deserialization handles multi-segment input

- **WHEN** `PrimitiveSerializers.Deserialize(seq, manifest)` is called with a string manifest and a multi-segment `ReadOnlySequence<byte>`
- **THEN** it SHALL decode the full UTF-8 sequence into a `string` correctly, handling segment boundaries that split UTF-8 codepoints

#### Scenario: Int32 / Int64 serialization

- **WHEN** `PrimitiveSerializers.Serialize(buffer, int)` or `Serialize(buffer, long)` is called
- **THEN** it SHALL write the value in little-endian format (matching the V1 `BitConverter.GetBytes` output on a little-endian platform — wire format is deliberately host-endian-equivalent and unchanged from V1)

#### Scenario: Int32 / Int64 deserialization handles multi-segment input

- **WHEN** `PrimitiveSerializers.Deserialize(seq, manifest)` is called with an int32 or int64 manifest and a `ReadOnlySequence<byte>` that splits the value across segments
- **THEN** it SHALL read the value correctly via `SequenceReader<byte>`

#### Scenario: All six manifest aliases supported

- **WHEN** `PrimitiveSerializers.Deserialize` receives any of the manifests `S`, `I`, `L`, `System.String, System.Private.CoreLib`, `System.Int32, System.Private.CoreLib`, `System.Int64, System.Private.CoreLib`, `System.String, mscorlib`, `System.Int32, mscorlib`, `System.Int64, mscorlib`
- **THEN** it SHALL dispatch to the correct primitive type and decode successfully

#### Scenario: Wire format byte-identical to V1

- **WHEN** the same primitive value (string / int / long) is serialized through V1 `PrimitiveSerializers.ToBinary` and through V2 `PrimitiveSerializers.Serialize`
- **THEN** the produced byte sequences SHALL be equal

### Requirement: Transport-envelope benchmark validates V2 API

The change SHALL include a standalone benchmark in `src/benchmark/` that exercises the V2 API on a synthetic Akka.Remote-shaped envelope and compares V2-direct against the V1-bridge baseline.

#### Scenario: Synthetic envelope round trip

- **WHEN** the benchmark serializes an envelope `[serializerId][manifest][payload]` via `Serialize(IBufferWriter)`, wraps the result as a `ReadOnlySequence<byte>`, and deserializes it via `Deserialize(ReadOnlySequence, manifest)`
- **THEN** the deserialized payload SHALL equal the original input

#### Scenario: V1-bridge baseline comparison

- **WHEN** the same envelope is round-tripped via the `ToBinary` / `FromBinary` bridge methods
- **THEN** the benchmark SHALL report allocation and throughput numbers for both paths so the V2-direct delta is measurable

#### Scenario: Coverage of all reference serializer paths

- **WHEN** the benchmark runs
- **THEN** it SHALL include at minimum: a string payload (short, medium, long), an int32 payload, an int64 payload, and a byte[] payload (small, medium, large)
