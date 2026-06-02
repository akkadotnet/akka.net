## ADDED Requirements

### Requirement: Artery remoting is separate from classic remoting

The system SHALL provide an Artery-style remoting implementation beside the existing classic remoting implementation.

#### Scenario: Artery selected by configuration
- **WHEN** Artery remoting is enabled in configuration
- **THEN** the remote actor ref provider SHALL use `ArteryRemoting`

#### Scenario: Classic remoting preserved
- **WHEN** Artery remoting is disabled
- **THEN** classic remoting SHALL continue to operate independently

### Requirement: Artery TCP framing

Artery TCP SHALL use a connection header and length-prefixed frames.

#### Scenario: Connection header
- **WHEN** a TCP stream is opened
- **THEN** the sender SHALL write magic bytes `AKKA` followed by a 1-byte stream ID

#### Scenario: Frame header
- **WHEN** an envelope is written
- **THEN** it SHALL be preceded by a 4-byte little-endian frame length

#### Scenario: Oversized frame rejected
- **WHEN** a frame length exceeds the configured maximum frame size
- **THEN** the connection SHALL be closed with an error

### Requirement: Artery envelope uses SerializerV2 payloads

Artery envelopes SHALL carry remoting metadata separately from the `SerializerV2` payload bytes.

#### Scenario: Envelope encode
- **WHEN** a user message is sent
- **THEN** the envelope SHALL encode origin UID, serializer ID, manifest, recipient, sender, and payload bytes

#### Scenario: Envelope decode before payload deserialization
- **WHEN** an envelope is received
- **THEN** remoting metadata SHALL be decoded before payload deserialization

### Requirement: Handshake establishes UID-scoped association

Artery remoting SHALL establish remote address and UID before accepting ordinary messages.

#### Scenario: Handshake request
- **WHEN** an outbound association starts
- **THEN** it SHALL send a handshake request containing the local address and UID

#### Scenario: Handshake response
- **WHEN** an inbound node accepts a handshake
- **THEN** it SHALL reply with its local address and UID

#### Scenario: Wrong target rejected
- **WHEN** a handshake is addressed to the wrong local address
- **THEN** it SHALL be rejected or dropped with a warning

#### Scenario: UID change resets association state
- **WHEN** the same remote address appears with a new UID
- **THEN** UID-scoped state SHALL be reset

### Requirement: Control stream separated from ordinary traffic

Control traffic SHALL use a control stream that cannot be starved by ordinary user messages.

#### Scenario: Control messages routed separately
- **WHEN** handshake, liveness, quarantine, or system-message ACK/NACK messages are sent
- **THEN** they SHALL use the control stream

#### Scenario: Ordinary messages use ordinary stream
- **WHEN** user actor messages are sent
- **THEN** they SHALL use the ordinary stream unless later large-message routing applies

### Requirement: Reliable system-message delivery

System messages SHALL be delivered with explicit ACK/NACK and resend semantics independent from ordinary user traffic.

#### Scenario: System message sequenced
- **WHEN** a system message is sent
- **THEN** it SHALL be assigned a monotonically increasing sequence number for the association incarnation

#### Scenario: System message acknowledged
- **WHEN** a receiver processes a system message in order
- **THEN** it SHALL acknowledge the sequence number over the control stream

#### Scenario: Missing system message nacked
- **WHEN** a receiver observes a gap in system-message sequence numbers
- **THEN** it SHALL send a negative acknowledgement for the highest processed sequence number

#### Scenario: System message resend
- **WHEN** a system message remains unacknowledged past the resend interval
- **THEN** it SHALL be resent until acknowledged or the give-up policy triggers

#### Scenario: System message overflow quarantines
- **WHEN** the bounded system-message buffer overflows
- **THEN** the association SHALL be quarantined

### Requirement: Outbound queues are bounded

Artery remoting SHALL use bounded outbound queues for user and control traffic.

#### Scenario: Ordinary queue overflow deterministic
- **WHEN** the ordinary outbound queue is full
- **THEN** the message SHALL be dropped or failed according to documented policy without unbounded memory growth

#### Scenario: Control queue overflow severe
- **WHEN** the control/system outbound queue is full
- **THEN** the association SHALL fail or quarantine according to documented policy
