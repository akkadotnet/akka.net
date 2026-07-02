## ADDED Requirements

### Requirement: Remote uses Akka.Streams TCP substrate

The system SHALL use Akka.Streams and Akka.IO.Tcp as the transport substrate for the redesigned Remote protocol pipeline and SHALL NOT introduce a separate raw socket or standalone pipe transport for this change.

#### Scenario: Stream substrate selected
- **WHEN** the redesigned Remote pipeline opens a TCP association
- **THEN** it SHALL use Akka.Streams / Akka.IO.Tcp transport infrastructure for TCP I/O
- **AND** it SHALL keep socket and pipe ownership inside the existing Akka.IO.Tcp transport layer

#### Scenario: No duplicate pipe transport
- **WHEN** the redesigned Remote pipeline is implemented
- **THEN** it SHALL NOT add a new transport whose primary purpose is duplicating Akka.IO.Tcp socket, stream, or pipe mechanics

### Requirement: Existing Remote wire format is preserved

The system SHALL preserve the current Akka.Remote wire format for the first production slice, including length framing, protobuf Akka protocol PDUs, protobuf `AckAndEnvelopeContainer`, serializer ids, manifests, and payload bytes.

#### Scenario: Existing PDU bytes remain decodable
- **WHEN** the redesigned pipeline receives bytes produced by the current Akka.Remote protobuf PDU codec
- **THEN** it SHALL decode the same control or payload semantic value as the current implementation

#### Scenario: New PDU bytes remain legacy-decodable
- **WHEN** the redesigned pipeline emits protobuf PDU bytes in wire-compatible mode
- **THEN** the current Akka.Remote protobuf PDU codec SHALL decode those bytes as the same control or payload semantic value

#### Scenario: MessagePack PDU envelope not enabled by default
- **WHEN** the redesigned pipeline is enabled for its first production slice
- **THEN** it SHALL NOT require MessagePack PDU envelopes or a new PDU wire format

### Requirement: PDU codec supports sequence and writer APIs

The system SHALL provide a PDU codec path that reads from `ReadOnlySequence<byte>` and writes to `IBufferWriter<byte>` while preserving compatibility adapters for existing `ByteString` callers during migration.

#### Scenario: Decode from sequence
- **WHEN** the PDU codec receives a `ReadOnlySequence<byte>` containing an Akka.Remote PDU
- **THEN** it SHALL decode the PDU without requiring callers to first materialize a `ByteString`

#### Scenario: Encode to buffer writer
- **WHEN** the PDU codec writes an Akka.Remote PDU
- **THEN** it SHALL be able to write the encoded bytes to a caller-owned `IBufferWriter<byte>`

#### Scenario: ByteString adapter remains compatible
- **WHEN** existing Remote code calls the legacy `ByteString` PDU codec API during migration
- **THEN** the adapter SHALL preserve existing behavior and wire output

### Requirement: Remote payloads use SerializerV2 boundary

The redesigned Remote payload path SHALL use `SerializerV2` as the uniform serializer abstraction and SHALL rely on `SerializerV1Adapter` for legacy serializers.

#### Scenario: V2 serializer writes payload
- **WHEN** a Remote outbound payload is handled by a native `SerializerV2`
- **THEN** the payload path SHALL write through the V2 `IBufferWriter<byte>` serialization API where the surrounding wire format permits it

#### Scenario: V2 serializer reads payload
- **WHEN** a Remote inbound payload is handled by a native `SerializerV2`
- **THEN** the payload path SHALL deserialize from `ReadOnlySequence<byte>` without first copying to a byte array where the surrounding wire format permits it

#### Scenario: Legacy serializer uses adapter
- **WHEN** a Remote payload is handled by a legacy V1 serializer
- **THEN** the payload path SHALL access it through `SerializerV1Adapter`
- **AND** unavoidable byte-array copies SHALL remain localized inside that adapter path

### Requirement: Protocol state machine preserves Remote semantics

The redesigned protocol pipeline SHALL preserve current Akka.Remote handshake, heartbeat, disassociation, quarantine, `refuseUid`, reliable-delivery, and listener-registration semantics.

#### Scenario: Handshake semantics preserved
- **WHEN** a redesigned association completes its handshake
- **THEN** it SHALL expose the same local address, remote address, and remote UID semantics as the current `AkkaProtocolTransport`

#### Scenario: Disassociation reason preserved
- **WHEN** a redesigned association receives or emits a disassociation reason
- **THEN** it SHALL surface the same `DisassociateInfo` semantics as the current protocol state actor

#### Scenario: Quarantine semantics preserved
- **WHEN** a redesigned association detects a quarantined remote UID or a refused UID
- **THEN** it SHALL produce the same quarantine behavior expected by `EndpointManager`

#### Scenario: Reliable delivery semantics preserved
- **WHEN** a redesigned association carries ack, nack, sequence number, or pure ack frames
- **THEN** it SHALL preserve the same reliable-delivery semantics as the current `AckAndEnvelopeContainer` path

### Requirement: Benchmarks gate each pipeline milestone

The system SHALL record baseline and regression benchmarks for Remote protocol pipeline changes before replacing hot-path internals.

#### Scenario: Baseline captured before implementation
- **WHEN** implementation begins
- **THEN** current RemotePingPong and PDU codec benchmark results SHALL be captured in the change notes

#### Scenario: Codec milestone benchmarked
- **WHEN** the PDU codec API is reshaped around sequence and writer APIs
- **THEN** the PDU codec benchmark SHALL report throughput and allocation deltas against the baseline

#### Scenario: End-to-end milestone benchmarked
- **WHEN** an opt-in redesigned Remote protocol pipeline can run end-to-end
- **THEN** RemotePingPong SHALL report throughput and allocation deltas against the baseline
