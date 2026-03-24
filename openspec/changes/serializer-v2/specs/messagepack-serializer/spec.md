## ADDED Requirements

### Requirement: MessagePackSerializer base class
The `Akka.Serialization.V2` package SHALL provide `MessagePackSerializer : SerializerV2` that bridges the `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API to typed `AkkaWriter` / `AkkaReader` methods.

#### Scenario: Serialize bridges to AkkaWriter
- **WHEN** `MessagePackSerializer.Serialize(buffer, obj)` is called
- **THEN** it SHALL create an `AkkaWriter(buffer)` and call the abstract `Write(AkkaWriter, obj)` method

#### Scenario: Deserialize bridges to AkkaReader
- **WHEN** `MessagePackSerializer.Deserialize(buffer, manifest)` is called
- **THEN** it SHALL create an `AkkaReader(buffer)` and call the abstract `Read(AkkaReader, manifest)` method

#### Scenario: Protocol-scoped generic variant
- **WHEN** a user defines `MySerializer : MessagePackSerializer<IMyProtocol>`
- **THEN** the `TProtocol` type parameter SHALL scope which message types belong to this serializer (used by future source generator)

### Requirement: Sealed AkkaWriter wraps MessagePack
The `AkkaWriter` class SHALL be sealed and wrap an `IBufferWriter<byte>` with typed write methods backed by MessagePack encoding.

#### Scenario: Write primitive types
- **WHEN** `AkkaWriter.WriteInt32(value)`, `WriteInt64(value)`, `WriteString(value)`, `WriteBool(value)`, `WriteDouble(value)` are called
- **THEN** they SHALL encode the values in MessagePack format directly to the underlying `IBufferWriter<byte>`

#### Scenario: Write complex types
- **WHEN** `AkkaWriter.WriteDateTime(value)`, `WriteDateTimeOffset(value)`, `WriteGuid(value)`, `WriteDecimal(value)` are called
- **THEN** they SHALL encode using the conventions defined in the POC (DateTime as [ticks, Kind], Guid as 16-byte binary, Decimal as [lo, mid, hi, flags])

#### Scenario: BeginObject for structured writes
- **WHEN** `AkkaWriter.BeginObject(fieldCount)` is called
- **THEN** it SHALL write a MessagePack array header enabling structured field-by-field serialization

#### Scenario: RawBuffer escape hatch
- **WHEN** advanced MessagePack scenarios require direct buffer access
- **THEN** `AkkaWriter.RawBuffer` SHALL expose the underlying `IBufferWriter<byte>`

### Requirement: Sealed AkkaReader wraps MessagePack
The `AkkaReader` class SHALL be sealed and wrap a `ReadOnlyMemory<byte>` (from the `ReadOnlySequence<byte>` input) with typed read methods backed by MessagePack decoding, tracking consumed byte offset.

#### Scenario: Read primitive types
- **WHEN** `AkkaReader.ReadInt32()`, `ReadInt64()`, `ReadString()`, `ReadBool()`, `ReadDouble()` are called
- **THEN** they SHALL decode MessagePack values and advance the internal consumed offset

#### Scenario: Read complex types
- **WHEN** `AkkaReader.ReadDateTime()`, `ReadDateTimeOffset()`, `ReadGuid()`, `ReadDecimal()` are called
- **THEN** they SHALL decode using the same conventions as AkkaWriter

#### Scenario: Version tolerance via SkipField
- **WHEN** `AkkaReader.SkipField()` is called for unknown trailing fields (newer message version)
- **THEN** it SHALL skip the next MessagePack value without decoding, advancing the offset

#### Scenario: TryReadNull for nullable fields
- **WHEN** `AkkaReader.TryReadNull()` is called and the next value is null
- **THEN** it SHALL consume the null and return true; otherwise leave position unchanged and return false

### Requirement: Serialization attributes
The `Akka.Serialization.V2` package SHALL provide attributes for marking serializable types and their serializers.

#### Scenario: AkkaSerializable attribute marks message types
- **WHEN** a class or record is annotated with `[AkkaSerializable]`
- **THEN** it SHALL be recognized as a type that can be serialized by a V2 MessagePack serializer

#### Scenario: AkkaField attribute marks serializable properties
- **WHEN** a property is annotated with `[AkkaField(index)]`
- **THEN** it SHALL be included in serialization at the specified field index (used for version tolerance)

#### Scenario: AkkaSerializer attribute marks serializer classes
- **WHEN** a partial class is annotated with `[AkkaSerializer]`
- **THEN** it SHALL be recognized as a target for future source generator code emission
