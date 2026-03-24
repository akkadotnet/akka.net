## ADDED Requirements

### Requirement: SerializerV2 base class with buffer API
The system SHALL provide an abstract `SerializerV2` class in core Akka with `Serialize(IBufferWriter<byte>, object)` and `Deserialize(ReadOnlySequence<byte>, string)` as the primary API. It SHALL NOT extend `Serializer` or `SerializerWithStringManifest`.

#### Scenario: Serialize to IBufferWriter
- **WHEN** `SerializerV2.Serialize(buffer, obj)` is called
- **THEN** the serializer SHALL write the serialized bytes directly into the provided `IBufferWriter<byte>` without allocating an intermediate `byte[]`

#### Scenario: Deserialize from ReadOnlySequence
- **WHEN** `SerializerV2.Deserialize(buffer, manifest)` is called with a `ReadOnlySequence<byte>`
- **THEN** the serializer SHALL read from the sequence and return the deserialized object

#### Scenario: ToBinary bridge method
- **WHEN** `SerializerV2.ToBinary(obj)` is called
- **THEN** it SHALL create an `ArrayBufferWriter<byte>`, call `Serialize()`, and return the written bytes as `byte[]`

#### Scenario: FromBinary bridge method
- **WHEN** `SerializerV2.FromBinary(bytes, manifest)` is called
- **THEN** it SHALL wrap the `byte[]` in `new ReadOnlySequence<byte>(bytes)` and call `Deserialize()`

### Requirement: SerializerV1Adapter wraps legacy serializers
The system SHALL provide `SerializerV1Adapter : SerializerV2` that wraps any `Serializer` or `SerializerWithStringManifest` instance to participate in the V2 infrastructure.

#### Scenario: V1 serializer wrapped for V2 dispatch
- **WHEN** a V1 `Serializer` is registered via HOCON or `SerializationSetup`
- **THEN** `Serialization.cs` SHALL auto-wrap it in `SerializerV1Adapter` for internal storage

#### Scenario: Adapter delegates to V1 ToBinary/FromBinary
- **WHEN** `SerializerV1Adapter.Serialize(buffer, obj)` is called
- **THEN** it SHALL call the inner V1 serializer's `ToBinary(obj)` and write the resulting bytes to the buffer

#### Scenario: Adapter preserves serializer identity
- **WHEN** `SerializerV1Adapter.Identifier` is accessed
- **THEN** it SHALL return the inner V1 serializer's `Identifier`

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
- **THEN** it SHALL look up the `SerializerV2` by ID and call `FromBinary(bytes, manifest)` (bridge method)

### Requirement: MessageSerializer uses V2 dispatch
The `MessageSerializer` in Akka.Remote SHALL use V2 serializer dispatch, calling `Manifest()` directly on `SerializerV2` without type-checking for `SerializerWithStringManifest`.

#### Scenario: Serialize with manifest
- **WHEN** `MessageSerializer.Serialize(system, transportInfo, message)` is called
- **THEN** it SHALL call `serializer.Manifest(message)` directly (all V2 serializers have manifests) and include it in the wire message

### Requirement: Internal Protobuf serializers ported to V2
Simple internal Protobuf serializers SHALL extend `SerializerV2` directly, using `proto.WriteTo(IBufferWriter<byte>)` and `Parser.ParseFrom(ReadOnlySequence<byte>)`. Wire format and serializer IDs SHALL remain unchanged.

#### Scenario: ClusterMessageSerializer uses V2 base
- **WHEN** `ClusterMessageSerializer` serializes a cluster message
- **THEN** it SHALL write Protobuf bytes directly to `IBufferWriter<byte>` via `proto.WriteTo()`, producing byte-identical output to the V1 implementation

#### Scenario: Deserialization uses ReadOnlySequence
- **WHEN** a Protobuf serializer deserializes a message
- **THEN** it SHALL parse from `ReadOnlySequence<byte>` via `Parser.ParseFrom()`, producing the same object as the V1 implementation

#### Scenario: Serializer IDs preserved
- **WHEN** an internal serializer is ported to V2
- **THEN** its `Identifier` property SHALL return the same integer ID as the V1 version
