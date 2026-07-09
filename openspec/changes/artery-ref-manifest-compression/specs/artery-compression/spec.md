## ADDED Requirements

### Requirement: Receiver-driven compression tables

Artery SHALL learn which actor refs and class manifests to compress from observed inbound traffic on the receiving node, and the receiving node SHALL be the authority that assigns compression indices.

#### Scenario: Heavy-hitter observed
- **WHEN** the Decoder resolves the sender ref, recipient ref, or class manifest of an inbound ordinary message
- **THEN** the receiver SHALL record a hit for that value against the sending system's origin UID, excluding temporary/promise actor refs and empty manifests

#### Scenario: Index assignment is dense and receiver-owned
- **WHEN** the receiver builds a compression table from its current heavy hitters
- **THEN** it SHALL assign gap-less indices starting at 0, up to the configured per-category maximum

### Requirement: Compression tables are versioned per origin UID

Compression tables SHALL be versioned and scoped to the sending system's 64-bit origin UID, and both directions SHALL carry the table version on the wire.

#### Scenario: Table version on the wire
- **WHEN** an envelope carries a COMPRESSED sender/recipient tag or a COMPRESSED manifest tag
- **THEN** the fixed header's actor-ref table-version byte (for sender/recipient) and manifest table-version byte (for manifest) SHALL identify the table the index belongs to

#### Scenario: Version numbering
- **WHEN** a new table version is issued
- **THEN** it SHALL be a value in `0..127` that wraps from `127` to `0`, with `-1` reserved to mean "compression disabled"

#### Scenario: Origin UID scoping
- **WHEN** the receiver resolves a COMPRESSED index
- **THEN** it SHALL select the decompression table by the envelope's origin UID and table-version byte, independent of any other origin's tables

### Requirement: Control-stream table advertisement

The receiver SHALL advertise a newly built compression table to the sender over the control stream, and the sender SHALL acknowledge it.

#### Scenario: Advertisement sent
- **WHEN** the advertisement schedule fires for an origin whose association is established and not quarantined
- **THEN** the receiver SHALL send a compression-table advertisement (actor-ref or class-manifest) over the control stream carrying its local unique address, the origin UID, the table version, and the index↔value mappings

#### Scenario: Advertisement installed and acknowledged
- **WHEN** a sender receives a compression-table advertisement from a remote system
- **THEN** it SHALL install the table as its outbound compression table for that destination AND reply with an advertisement Ack carrying the table version

#### Scenario: Control and handshake traffic never compressed
- **WHEN** a control-stream or handshake (`ArteryMessage`) message is encoded
- **THEN** its refs and manifest SHALL be written as LITERAL/ABSENT regardless of any installed compression table

#### Scenario: Advertisement is resent until confirmed
- **WHEN** an advertisement has been sent but not yet confirmed
- **THEN** the receiver SHALL resend it up to a bounded retry count, and give up (ceasing to advertise that version) after the bound is exceeded

### Requirement: Table confirmation and rotation

The receiver SHALL begin using a newly advertised table only once it is confirmed, and SHALL retain a bounded number of superseded tables so in-flight messages still decode.

#### Scenario: Confirmed by first stamped message
- **WHEN** the receiver decodes the first inbound message whose table-version byte equals the advertised (in-progress) version
- **THEN** it SHALL activate the advertised table as its current decompression table for that origin

#### Scenario: Confirmed by explicit Ack
- **WHEN** the receiver processes an advertisement Ack for the in-progress version
- **THEN** it SHALL activate the advertised table even if no compressed message has yet arrived

#### Scenario: Old tables retained
- **WHEN** a new table is activated
- **THEN** a bounded number of previously active tables SHALL be retained and remain usable for decoding messages still stamped with their versions

### Requirement: Compressed encode and decode

The Encoder SHALL emit a COMPRESSED tag for a ref/manifest present in its active outbound table, and the Decoder SHALL resolve a COMPRESSED index through the matching per-origin decompression table.

#### Scenario: Compressed sender/recipient/manifest emitted
- **WHEN** a ref or manifest being encoded is present in the sender's active outbound compression table
- **THEN** the Encoder SHALL write a COMPRESSED tag carrying the 16-bit table index and stamp the corresponding table-version byte, and SHALL NOT write the literal

#### Scenario: Fallback to literal
- **WHEN** a ref or manifest being encoded is absent from the active outbound table
- **THEN** the Encoder SHALL write a LITERAL tag exactly as it does today

#### Scenario: Compressed index resolved
- **WHEN** the Decoder reads a COMPRESSED tag
- **THEN** it SHALL resolve the value from the decompression table selected by origin UID and table-version byte

#### Scenario: Unknown or stale index dropped, not crashed
- **WHEN** the Decoder reads a COMPRESSED tag whose table version is unknown for that origin (e.g. built for a previous incarnation of this system, or already rotated out)
- **THEN** the message SHALL be dropped with a warning rather than faulting the stream, and the receiver SHALL advertise a fresh table

### Requirement: Compression is optional and off by default

Compression SHALL be configurable and SHALL default off until the full loop and its tests exist.

#### Scenario: Disabled path unchanged
- **WHEN** compression is disabled by configuration
- **THEN** the Encoder SHALL only ever emit ABSENT/LITERAL tags and the Decoder SHALL never expect a COMPRESSED tag

#### Scenario: Interop with a non-advertising peer
- **WHEN** a compression-enabled node has received no advertisement from a peer
- **THEN** it SHALL send LITERAL tags to that peer and continue to interoperate normally
