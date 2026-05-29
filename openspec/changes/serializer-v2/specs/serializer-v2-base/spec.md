## ADDED Requirements

### Requirement: SerializerV2 base class with buffer-first API

The system SHALL provide an abstract `SerializerV2` class in core Akka that is the canonical Akka.NET 1.6 serializer abstraction.

#### Scenario: V2 does not inherit from V1
- **WHEN** `SerializerV2` is defined
- **THEN** it SHALL NOT extend `Serializer` or `SerializerWithStringManifest`

#### Scenario: Serialize to IBufferWriter
- **WHEN** `SerializerV2.Serialize(...)` is called with an `IBufferWriter<byte>`
- **THEN** the serializer SHALL write serialized bytes into the provided writer without requiring an intermediate `byte[]`

#### Scenario: Serialize reports bytes written
- **WHEN** `SerializerV2.Serialize(...)` completes
- **THEN** the caller SHALL be able to determine how many payload bytes were written

#### Scenario: Deserialize from ReadOnlySequence
- **WHEN** `SerializerV2.Deserialize(...)` is called with a `ReadOnlySequence<byte>`
- **THEN** the serializer SHALL read from the sequence and return the deserialized object

#### Scenario: Manifest is direct V2 API
- **WHEN** a caller needs the manifest for an object
- **THEN** it SHALL call a direct V2 manifest API without type-checking for `SerializerWithStringManifest`

#### Scenario: Unknown size hint
- **WHEN** a serializer cannot cheaply predict serialized size
- **THEN** `SizeHint` SHALL return an explicit unknown-size value rather than a misleading estimate

#### Scenario: ToBinary bridge method
- **WHEN** compatibility code calls `SerializerV2.ToBinary(obj)`
- **THEN** it SHALL return a `byte[]` containing the serialized payload

#### Scenario: FromBinary bridge method
- **WHEN** compatibility code calls `SerializerV2.FromBinary(bytes, manifest)`
- **THEN** it SHALL deserialize the payload using the V2 deserialization path

### Requirement: SerializerV1Adapter wraps legacy serializers

The system SHALL provide `SerializerV1Adapter : SerializerV2` that wraps any legacy `Serializer` or `SerializerWithStringManifest` instance.

#### Scenario: V1 serializer wrapped for V2 dispatch
- **WHEN** a V1 serializer is registered via HOCON or `SerializationSetup`
- **THEN** `Serialization.cs` SHALL wrap it in `SerializerV1Adapter` before storing it internally

#### Scenario: Adapter preserves serializer identifier
- **WHEN** `SerializerV1Adapter.Identifier` is accessed
- **THEN** it SHALL return the wrapped serializer's `Identifier`

#### Scenario: Adapter preserves manifest behavior
- **WHEN** `SerializerV1Adapter` is asked for a manifest
- **THEN** it SHALL preserve the wrapped serializer's string-manifest or type-manifest behavior

#### Scenario: Adapter exposes inner serializer
- **WHEN** migration code or tests need the original V1 serializer
- **THEN** `SerializerV1Adapter.Inner` SHALL return it

### Requirement: Serialization uses V2 internally

The `Serialization` class SHALL store and return `SerializerV2` instances.

#### Scenario: FindSerializerFor returns V2
- **WHEN** `Serialization.FindSerializerFor(msg)` is called
- **THEN** it SHALL return `SerializerV2`

#### Scenario: FindSerializerForType returns V2
- **WHEN** `Serialization.FindSerializerForType(type)` is called
- **THEN** it SHALL return `SerializerV2`

#### Scenario: Deserialize dispatches through V2
- **WHEN** `Serialization.Deserialize(bytes, serializerId, manifest)` is called
- **THEN** it SHALL look up the V2 serializer by ID and deserialize through V2

#### Scenario: Missing serializer errors remain actionable
- **WHEN** a serializer ID cannot be found
- **THEN** the thrown error SHALL include the serializer ID and existing serializer error guidance

### Requirement: Classic remoting uses V2 while preserving wire compatibility

Classic Akka.Remote SHALL use `SerializerV2` for payload serialization and deserialization while preserving the existing classic protobuf wire format.

#### Scenario: Classic remoting serializes V1 payload through adapter
- **WHEN** a V1-serialized message is sent over classic remoting
- **THEN** the message SHALL be serialized through `SerializerV1Adapter` and encoded in the existing classic wire envelope

#### Scenario: Classic remoting serializes native V2 payload
- **WHEN** a native V2-serialized message is sent over classic remoting
- **THEN** the message SHALL be serialized through V2 and encoded in the existing classic wire envelope

#### Scenario: Classic remoting reads old payloads
- **WHEN** classic remoting receives an existing protobuf `Payload`
- **THEN** it SHALL deserialize the payload through V2 by serializer ID and manifest

### Requirement: Akka.Persistence uses V2 while preserving stored data compatibility

Akka.Persistence SHALL use `SerializerV2` for event and snapshot payloads while preserving the existing stored serializer ID + manifest + payload model.

#### Scenario: Old journal event remains readable
- **WHEN** a journal event written with a V1 serializer is replayed
- **THEN** it SHALL deserialize successfully through V2 infrastructure

#### Scenario: Old snapshot remains readable
- **WHEN** a snapshot written with a V1 serializer is loaded
- **THEN** it SHALL deserialize successfully through V2 infrastructure

#### Scenario: New V2 event persists and recovers
- **WHEN** an event uses a native V2 serializer
- **THEN** persistence SHALL store serializer ID, manifest, and payload bytes and recover the original event

#### Scenario: New V2 snapshot saves and loads
- **WHEN** a snapshot uses a native V2 serializer
- **THEN** persistence SHALL store serializer ID, manifest, and payload bytes and load the original snapshot
