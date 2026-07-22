## ADDED Requirements

### Requirement: Migrated subsystems write MessagePack by default, with legacy readers registered forever

Once a subsystem's migration flips its `reference.conf` `serialization-bindings` entry, the system SHALL write that subsystem's messages with the forked MessagePack V2 serializer by default, and SHALL always register both the legacy protobuf serializer and the MessagePack V2 serializer (each under its own stable id) regardless of which one the binding selects.

#### Scenario: Flipped subsystem writes MessagePack V2 by default

- **WHEN** a subsystem's shipped binding has flipped and no operator override is present
- **THEN** the system SHALL write that subsystem's messages using the forked MessagePack V2 serializer

#### Scenario: Both formats remain readable regardless of the binding

- **WHEN** a node receives a message serialized with either the legacy protobuf serializer id or the MessagePack V2 serializer id for a migrated subsystem
- **THEN** the node SHALL deserialize the message successfully, independent of which serializer the node's own binding currently selects

### Requirement: Operator opt-out through standard binding overrides

The system SHALL honor a standard `application.conf` `serialization-bindings` override pinning a migrated subsystem's marker interface back to the legacy serializer, and SHALL NOT introduce any new configuration surface (feature flags, switches, or registries) for selecting a subsystem's write format.

#### Scenario: Binding override restores legacy writes

- **WHEN** an operator overrides a migrated subsystem's marker-interface binding to the legacy serializer name in `application.conf`
- **THEN** the system SHALL write that subsystem's messages using the legacy protobuf serializer
- **AND** reads of both wire formats SHALL continue to succeed

### Requirement: Reserved internal serializer-id block

The system SHALL assign each forked MessagePack V2 serializer a serializer id in the reserved range 40-79, computed as the corresponding legacy serializer's id plus 40.

#### Scenario: Forked serializer id follows the legacy-plus-40 mapping

- **WHEN** a built-in subsystem serializer is forked to a MessagePack V2 serializer
- **THEN** the new serializer's id SHALL equal the legacy serializer's id plus 40
- **AND** the new serializer's id SHALL fall within the 40-79 range

### Requirement: Durable records are self-describing and recover by stored format

Every durable record the migration touches SHALL carry a signal of the serializer that wrote it, and recovery SHALL dispatch on that signal rather than on the current write-side binding, so that records written before and after a subsystem's flip both recover correctly.

#### Scenario: Legacy headerless LMDB records recover after the DData flip

- **WHEN** DistributedData's binding has flipped to the MessagePack V2 serializer and the durable (LMDB) store contains records written before this change (no format header)
- **THEN** the store SHALL recover each headerless record as the legacy protobuf `DurableDataEnvelope`
- **AND** records written after the change SHALL carry a `(serializerId, manifest)` header and recover by that stored id

#### Scenario: Stamped persistence recovers old and new entries by stored id

- **WHEN** a durable store that stamps the serializer id per record (Akka.Persistence journals/snapshots behind `EventSourcedProducerQueue`, or Sharding remember-entities) contains a mix of pre-flip protobuf entries and post-flip MessagePack entries
- **THEN** recovery SHALL deserialize each entry using its own stored serializer id and manifest, independent of the current write-side binding

### Requirement: Envelope and nested payloads are preserved as serializer boundaries

Forked MessagePack V2 wrapper serializers SHALL preserve a wrapped payload's own serializer id, manifest, and serialized bytes rather than re-encoding the payload structurally.

#### Scenario: Wrapped user payload round-trips without re-encoding

- **WHEN** a forked V2 wrapper serializer (for example a delivery `SequencedMessage` or a DistributedData `OtherMessage`/`DataEnvelope`) writes a wrapped application payload
- **THEN** it SHALL store the payload's serializer id, manifest, and opaque serialized bytes
- **AND** it SHALL recover the original payload through normal Akka deserialization using that stored id, manifest, and bytes

### Requirement: Benchmark acceptance gate governs binding-flip timing

The system's migrated subsystems SHALL be evaluated with matched protobuf-vs-MessagePack-V2 benchmarks reporting CPU cost, allocations, and payload size before that subsystem's shipped `reference.conf` binding flips to the MessagePack V2 serializer.

#### Scenario: Subsystem benchmark reports all three gate metrics

- **WHEN** a migrated subsystem's benchmark suite runs
- **THEN** it SHALL report serialize+deserialize CPU cost, allocated bytes, and payload size for both the legacy protobuf serializer and the forked MessagePack V2 serializer

#### Scenario: Subsystems migrate as a unit

- **WHEN** a subsystem's shipped binding flips
- **THEN** every message type handled by that subsystem's serializer SHALL be written with the forked MessagePack V2 serializer, with no per-message-type carve-out to the legacy protobuf binding
- **AND** any measured payload-size or CPU regression on individual message types SHALL be recorded in the benchmark results and addressed through serializer optimization, informing flip timing rather than which messages migrate

### Requirement: Rolling-upgrade safety through read-forever registration and a documented roll recipe

The system SHALL keep every v1.6 node able to read both wire formats at all times, and SHALL document that a rolling upgrade from a pre-v1.6 version — where a flipped subsystem is in use — requires pinning that subsystem's binding to the legacy serializer for the duration of the roll.

#### Scenario: Mixed v1.6 clusters interoperate without configuration

- **WHEN** some v1.6 nodes write MessagePack V2 for a subsystem and other v1.6 nodes write legacy protobuf for the same subsystem (for example mid-roll of a binding override change)
- **THEN** all nodes SHALL deserialize all of that subsystem's messages successfully

#### Scenario: A node without the V2 serializer registered cannot decode a V2 id

- **WHEN** a node that does not have a migrated subsystem's MessagePack V2 serializer registered (any pre-v1.6 node) receives a message with that serializer's id
- **THEN** deserialization SHALL fail with a "cannot find serializer with id" error, which is the documented reason the binding-override recipe MUST be applied during mixed pre-v1.6 rolls
