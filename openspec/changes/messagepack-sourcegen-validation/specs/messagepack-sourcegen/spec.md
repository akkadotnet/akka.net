## ADDED Requirements

### Requirement: MessagePack writer and reader package

The system SHALL provide an `Akka.Serialization.V2` package containing sealed `AkkaWriter` and `AkkaReader` types backed by MessagePack-CSharp.

#### Scenario: Writer uses V2 buffer API
- **WHEN** a generated serializer writes a message
- **THEN** it SHALL write MessagePack bytes through `AkkaWriter` into the provided `IBufferWriter<byte>`

#### Scenario: Reader uses V2 sequence API
- **WHEN** a generated serializer reads a message
- **THEN** it SHALL read MessagePack bytes through `AkkaReader` from the provided V2 input

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
