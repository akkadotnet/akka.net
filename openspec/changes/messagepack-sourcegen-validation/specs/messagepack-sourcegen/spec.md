## ADDED Requirements

### Requirement: MessagePack writer and reader package

The system SHALL provide an `Akka.Serialization.V2` package containing MessagePack-CSharp-backed helper conventions for generated serializers.

#### Scenario: Writer uses V2 buffer API
- **WHEN** a generated serializer writes a message
- **THEN** it SHALL write MessagePack bytes into the provided `IBufferWriter<byte>` using a cursor-based `MessagePackWriter`

#### Scenario: Reader uses V2 sequence API
- **WHEN** a generated serializer reads a message
- **THEN** it SHALL read MessagePack bytes from the provided V2 input using a cursor-based `MessagePackReader`

### Requirement: SerializerV2 size hints are exact or unknown

The system SHALL treat a non-negative `SerializerV2.SizeHint` result as the exact serialized byte size and SHALL use `SerializerV2.UnknownSize` when exact sizing cannot be proven cheaply.

#### Scenario: Generated serializer cannot prove exact size
- **WHEN** a generated serializer cannot compute the exact encoded size of every field it writes
- **THEN** `SizeHint` SHALL return `SerializerV2.UnknownSize`

#### Scenario: Generated serializer can prove exact size
- **WHEN** a generated serializer can compute the exact encoded size of every field it writes
- **THEN** `SizeHint` SHALL return the exact serialized byte count

#### Scenario: Unknown envelope payload size propagates
- **WHEN** a generated serializer contains an envelope payload whose payload serializer reports `SerializerV2.UnknownSize`
- **THEN** every enclosing generated serializer that includes that payload SHALL also report `SerializerV2.UnknownSize`

#### Scenario: Unknown-size serialization fallback
- **WHEN** `SizeHint` returns `SerializerV2.UnknownSize`
- **THEN** serialization SHALL still write valid bytes through the V2 `IBufferWriter<byte>` API

### Requirement: Source generator emits V2 serializers

The system SHALL provide a Roslyn incremental source generator that emits `SerializerV2` implementations for annotated messages.

#### Scenario: Serializable type annotated
- **WHEN** a type is annotated with `[AkkaSerializable]` and valid `[AkkaField]` members
- **THEN** the generator SHALL emit serializer code for that type

#### Scenario: Invalid schema rejected
- **WHEN** field indexes are missing, duplicated, or unsupported
- **THEN** the generator SHALL produce compile-time diagnostics

#### Scenario: Field IDs encoded
- **WHEN** a generated serializer writes a message
- **THEN** it SHALL encode each `[AkkaField]` index as an explicit field ID in the MessagePack payload

#### Scenario: Unknown field skipped
- **WHEN** a generated serializer reads a payload containing an unknown field ID
- **THEN** it SHALL skip that field and continue reading known fields

#### Scenario: Missing required field rejected
- **WHEN** a generated serializer reads a payload missing a non-nullable required field
- **THEN** deserialization SHALL fail with a serialization error

#### Scenario: Nullable field preserved
- **WHEN** a generated serializer writes or reads nullable scalar, reference, enum, or nested generated fields
- **THEN** MessagePack nil and missing optional fields SHALL deserialize as null while present values SHALL round-trip normally

#### Scenario: Byte array field preserved
- **WHEN** a generated serializer writes or reads a byte-array field
- **THEN** it SHALL encode the value as a MessagePack binary payload and preserve the original bytes

### Requirement: Generated serializers support envelope payload boundaries

Generated serializers SHALL support `[AkkaEnvelopePayload]` fields as Akka serializer boundaries rather than inline generated schemas.

#### Scenario: Envelope payload field serialized
- **WHEN** a generated serializer writes a field marked `[AkkaEnvelopePayload]`
- **THEN** it SHALL resolve the field value through normal Akka serializer lookup
- **AND** it SHALL store the payload serializer id, serializer manifest, and opaque serialized payload bytes

#### Scenario: Envelope payload field deserialized
- **WHEN** a generated serializer reads a non-null `[AkkaEnvelopePayload]` field
- **THEN** it SHALL recover the field value through normal Akka deserialization using the stored serializer id, manifest, and bytes

#### Scenario: V2 envelope payload deserialized without byte-array copy
- **WHEN** a generated serializer reads a non-null `[AkkaEnvelopePayload]` field whose payload serializer is V2
- **THEN** it SHALL dispatch the MessagePack `bin` payload as a `ReadOnlySequence<byte>` without first copying it into a byte array

#### Scenario: Unknown-size envelope payload serialized through staging buffer
- **WHEN** a generated serializer writes a non-null `[AkkaEnvelopePayload]` field whose serialized length is not known before writing the MessagePack `bin` header
- **THEN** it SHALL stage the inner payload bytes before writing the outer `bin` field

#### Scenario: Nested envelope payload chain
- **WHEN** generated envelopes contain nested `[AkkaEnvelopePayload]` fields
- **THEN** generated V2 payloads and custom V1 payloads SHALL round-trip without requiring structural MessagePack encoding for the inner payload object

### Requirement: Generated serializers use explicit registration

Generated serializers SHALL expose per-serializer registration helpers and SHALL NOT require runtime assembly scanning.

#### Scenario: Serializer declares protocol family
- **WHEN** a user declares a partial serializer class for a protocol marker interface
- **THEN** the generator SHALL attach discoverable registration helpers to that serializer class

#### Scenario: Multiple serializers registered
- **WHEN** an application uses serializers from multiple assemblies
- **THEN** the application SHALL compose per-serializer registrations explicitly into one `SerializationSetup`

### Requirement: Generated serializers support actor references

Generated serializers SHALL support `IActorRef` fields using Akka's transport-aware actor-ref serialization helpers.

#### Scenario: Actor reference field serialized
- **WHEN** a generated serializer writes an `IActorRef` field
- **THEN** it SHALL serialize the field using `Serialization.SerializedActorPath`

#### Scenario: Actor reference field deserialized
- **WHEN** a generated serializer reads an actor-ref path
- **THEN** it SHALL resolve the path using the serializer's `ExtendedActorSystem`

### Requirement: Generated serializers favor immutable message shapes

Generated serializers SHALL initially support immutable message designs and nested generated structures.

#### Scenario: Immutable constructor-bound message
- **WHEN** a message uses a record primary constructor or supported constructor-bound immutable shape
- **THEN** the generator SHALL emit read and write code for the message

#### Scenario: Nested generated type
- **WHEN** a message contains a nested generated type with explicit field IDs
- **THEN** the generator SHALL serialize and deserialize the nested structure without runtime reflection

#### Scenario: Multi-level nested generated type
- **WHEN** a message contains multiple levels of nested generated types with explicit field IDs
- **THEN** the generator SHALL serialize and deserialize the full nested structure inline without runtime reflection

#### Scenario: Nested value object without manifest
- **WHEN** a nested generated type is not a top-level protocol message
- **THEN** the generator SHALL serialize it inline without requiring or using a serializer manifest for that nested type

#### Scenario: Nested value object without serialization definition
- **WHEN** a message contains a nested value-object field that is not annotated for generated serialization
- **THEN** the generator SHALL fail compilation with a diagnostic

#### Scenario: Mutable or factory-only shape
- **WHEN** a message requires mutable setter-centric hydration, arbitrary factory methods, or unsupported polymorphic discovery
- **THEN** the generator SHALL reject it with a diagnostic

### Requirement: Generated serializers validate SerializerV2 API

Generated serializers SHALL validate `SerializerV2` through real Akka.NET integration points before Artery envelopes are implemented.

#### Scenario: Serialization round-trip
- **WHEN** a generated serializer is registered
- **THEN** the message SHALL round-trip through `Serialization.cs`

#### Scenario: Classic remoting round-trip
- **WHEN** a generated-serializer message is sent over classic Akka.Remote
- **THEN** the receiver SHALL deserialize the original message

#### Scenario: Persistence event round-trip
- **WHEN** a generated-serializer event is persisted
- **THEN** it SHALL recover as the original event

#### Scenario: Persistence snapshot round-trip
- **WHEN** a generated-serializer snapshot is saved
- **THEN** it SHALL load as the original snapshot

#### Scenario: V1 coexistence
- **WHEN** V1 and generated V2 serializers are registered in the same actor system
- **THEN** both SHALL work through the V2 serialization infrastructure

#### Scenario: Opaque non-MessagePack payload inside generated wrapper
- **WHEN** a generated MessagePack wrapper carries an application payload owned by a custom non-MessagePack Akka serializer
- **THEN** the wrapper SHALL store the payload serializer id, serializer manifest, and opaque serialized payload bytes
- **AND** the wrapper SHALL NOT require the inner payload type to be annotated for generated MessagePack serialization
- **AND** the inner payload SHALL be recoverable through normal Akka deserialization using the stored serializer id, manifest, and bytes

#### Scenario: Akka.Delivery wrapper validation
- **WHEN** a generated-serializer message is used as an Akka.Delivery payload
- **THEN** the delivery wrapper SHALL preserve the generated payload metadata and recover the original message

#### Scenario: DistributedData wrapper validation
- **WHEN** a generated-serializer message is used inside a DistributedData value where supported
- **THEN** the DistributedData wrapper SHALL preserve the generated payload metadata and recover the original message

### Requirement: Benchmark POC demonstrates protocol-family performance

The system SHALL include an early benchmark POC using real C# protocol-family message types before the full spec is completed.

#### Scenario: Generated serializer benchmarked
- **WHEN** the benchmark serializes and deserializes a real protocol-family message
- **THEN** it SHALL report generated MessagePack throughput/allocation/payload-size signals against an existing baseline serializer

#### Scenario: Envelope payload composition benchmarked
- **WHEN** the benchmark serializes generated MessagePack envelopes around generated V2 payloads, custom V1 payloads, same-shape custom V1 payloads, pre-captured payload metadata, and attribute-driven nested envelope payload fields
- **THEN** it SHALL report throughput/allocation/payload-size signals for capture, wrapper serialization, and recovery paths
